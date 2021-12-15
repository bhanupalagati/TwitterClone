#r "nuget: Suave"
// #load "twitterCore.fsx"
#r "nuget: Akka"
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#r "nuget: Newtonsoft.Json.FSharp"
#load "dataDefnitions.fs"

open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Suave
open Suave.Filters
open Suave.Operators
// open TwitterCore
open Akka.Actor
open Akka.FSharp
open DataDefnitions
open Newtonsoft.Json
open System.Text.RegularExpressions

open System
let mutable socketsSpawned = 1


let system = ActorSystem.Create "twitterEngine" 
// let mutable socketLink = 

open System.Data


let tweets = new DataTable()
let users = new DataTable()

let tweetsPKCol = new DataColumn("Id", typeof<string>)
tweets.Columns.Add(tweetsPKCol)
tweets.Columns.Add(new DataColumn("message", typeof<string>))
tweets.Columns.Add(new DataColumn("senderId", typeof<string>))
tweets.Columns.Add(new DataColumn("hashTags", typeof<string>))
tweets.Columns.Add(new DataColumn("mentions", typeof<string>))
tweets.Columns.Add(new DataColumn("asRetweet", typeof<bool>))
tweets.PrimaryKey = [| tweetsPKCol |]


users.Columns.Add(new DataColumn("Id", typeof<string>))
users.Columns.Add(new DataColumn("password", typeof<string>))

let buildTweets(selectRes: seq<DataRow>) =
    let tweetsResponse = selectRes |> Seq.map (fun ele -> {Id = string ele.["Id"]; message = string ele.["message"]; senderId = string ele.["senderId"]; hashTags = string ele.["hashTags"]; mentions = string ele.["mentions"]; asRetweet = bool.Parse(string ele.["asRetweet"])})
    tweetsResponse


let addTweets(tweet: tweet) = 
    lock(tweets) (fun () ->
        tweets.Rows.Add(tweet.Id, tweet.message, tweet.senderId, tweet.hashTags, tweet.mentions, tweet.asRetweet) |> ignore
    )

let queryTweets(query: query) =
    buildTweets(tweets.Select(String.Format("{0} Like '*{1}*'", query.searchOn, query.searchWith)))


let queryTweetsById(tweetId: string) = 
    buildTweets(tweets.Select(String.Format("Id = '{0}'", tweetId)))

let getAllTweets() = 
    buildTweets(tweets.Select())

let userActor (mailbox: Actor<Message>) =
    let mutable userId = ""
    let mutable password = ""
    let mutable followers: List<string> = []
    let mutable following: List<string> = []
    let mutable isLoggedIn = false
    let mutable websocketLink = Unchecked.defaultof<IActorRef>
    let coordinatorRef = select ("/user/coordinator") system
    let rec loop() = 
        actor {
            let! (msg:Message) = mailbox.Receive()
            let sender = mailbox.Sender()
            match msg with
            | Register (uid, pwd) -> 
                userId  <- uid
                password <- pwd
                let message = "User " + string userId + " successfully registered"
                sender <! RegisterSuccess {Message = message; Heading = "Registration Success"; From = "self"}
            | Login (uid, pwd) -> 
                if pwd = password then
                    isLoggedIn <- true
                    websocketLink <- mailbox.Sender()
                    sender <! LoginSuccess {Message = "User "+ string userId + " successfully loggedin"; Heading = "Login Success"; From = "self"}
                else
                    sender <! ErrorResp {Message = "Please provide valid username and password"; Heading = "Error! Invalid Credential"}    
            | Logout (uid, pwd) ->
                isLoggedIn <- false
                // websocketLink <- Unchecked.defaultof<IActorRef>
                sender <! LogoutSuccess {Message = "User "+ string userId + " successfully loggedOut"; Heading = "Logout Success"; From = "self"}
            | NewFollower follower ->
                followers <- [follower] @ followers
            | Subscribed userids ->
                if isLoggedIn then
                    for id in userids do
                        coordinatorRef <! FollowerNotification(id, userId)
                    following <- userids @ following
                    sender <! SubscriptionSuccess {Message = "Subscribed to "+ string userids.[0] + " successfully"; Heading = "Subscription Success"; From = "self"}
                else
                    sender <! ErrorResp {Message = "Please login to perform these actions"; Heading = "Not Logged In"}
            | Tweet tweet -> 
                if isLoggedIn then
                    let res = addTweets tweet
                    for uId in followers do
                        coordinatorRef <! TweetNotificationCarrer(string uId, tweet)
                    for mention in Regex(@"@\w+").Matches tweet.mentions do
                        let m = string mention
                        coordinatorRef <! TweetNotificationCarrer(string m.[1..], tweet)
                    sender <! TweetSuccess {Message = tweet; Heading = "Tweet Success";}
                else
                    sender <! ErrorResp {Message = "Please login to perform these actions"; Heading = "Not Logged In"}
            | ReTweet tweetId -> 
                if isLoggedIn then
                    let tweets = queryTweetsById(tweetId)
                    let mutable retweet = Unchecked.defaultof<tweet>;
                    for tweet in tweets do
                        retweet <- tweet
                        for uId in followers do
                            coordinatorRef <! TweetNotificationCarrer(string uId, {tweet with asRetweet = true})
                    sender <! ReTweetSuccess {Message = {retweet with asRetweet = true}; Heading = "ReTweet Success";}
                else
                    sender <! ErrorResp {Message = "Please login to perform these actions"; Heading = "Not Logged In"}
            | DM(message, senderId) -> 
                if isLoggedIn then
                    websocketLink <! DMSuccess {Message = message; From = senderId; Heading = "Message Received"}
                else
                    sender <! ErrorResp {Message = "Please login to perform these actions"; Heading = "Not Logged In"}
            | TweetNotification tweet ->
                websocketLink <! TweetReceived({Message = tweet; Heading = "New tweet received"})
            | Query query -> 
                if isLoggedIn then
                    let tweets = queryTweets query
                    sender <! QuerySuccess {Heading = "Querry Success"; Results = tweets}
                else
                    sender <! ErrorResp {Message = "Please login to perform these actions"; Heading = "Not Logged In"}
            return! loop()
        }
    loop()


let spawnUser userName = 
    spawn system userName userActor


let coordinator(mailbox: Actor<Coordinator>) = 
    let rec loop() = 
        actor {
            let! (msg: Coordinator) = mailbox.Receive()
            match msg with
            | CreateUsers(username) ->
                spawnUser(string username) |> ignore
            | FollowerNotification(following, followedBy) -> 
                let user = select ("/user/"+ following) system
                user <! NewFollower followedBy
            | TweetNotificationCarrer(notifyTo, tweet) -> 
                let user = select ("/user/"+ notifyTo) system
                user <! TweetNotification(tweet)
            return! loop()
        }
    loop()

let coordinatorRef = spawn system "coordinator" coordinator
let settings = new JsonSerializerSettings(TypeNameHandling = TypeNameHandling.All)
let sendws (webSocket : WebSocket) (context: HttpContext) (data: string) =
    let dosomething() = socket {
        let byteResponse =
            data
            |> System.Text.Encoding.ASCII.GetBytes
            |> ByteSegment
        do! webSocket.send Text byteResponse true
    }
    dosomething()

let task (webSocket : WebSocket) (context: HttpContext) (data: string) = 
    async {
        let! ws = sendws webSocket context data
        ws |> ignore       
    }

let WSActor (webSocket: WebSocket) (context: HttpContext) (mailbox: Actor<Commands>) = 
    let rec loop() = 
        actor {
            let! msg = mailbox.Receive()
            match msg with
            | Request req -> 
                printfn "%A" req
                let reqStr = string req
                let splits = reqStr.Split("\n")
                let request = splits.[1].Split("RequestAPI: ").[1]
                let data = reqStr.Substring(reqStr.IndexOf("Body: ") + 6)
                match request with
                | "Register" ->
                    let deserializedObj = JsonConvert.DeserializeObject<justUser>(data,settings)
                    coordinatorRef  <! CreateUsers(deserializedObj.userName)
                    Threading.Thread.Sleep(100)
                    let user = select ("/user/"+deserializedObj.userName) system
                    user <! Register(deserializedObj.userName, string deserializedObj.password)
                | "Login" ->
                    let deserializedObj = JsonConvert.DeserializeObject<justUser>(data,settings)
                    let user = select ("/user/"+deserializedObj.userName) system
                    user <! Login(deserializedObj.userName, string deserializedObj.password)
                | "Logout" ->
                    let deserializedObj = JsonConvert.DeserializeObject<justUser>(data,settings)
                    let user = select ("/user/"+deserializedObj.userName) system
                    user <! Logout(deserializedObj.userName, string deserializedObj.password)
                | "Subscribe" ->
                    let deserializedObj = JsonConvert.DeserializeObject<subscriberRec>(data,settings)
                    let user = select ("/user/"+deserializedObj.userName) system
                    user <! Subscribed([deserializedObj.subscriberId])
                | "Tweet" ->
                    let deserializedObj = JsonConvert.DeserializeObject<tweet>(data,settings)
                    let user = select ("/user/"+deserializedObj.senderId) system
                    user <! Tweet(deserializedObj)
                | "ReTweet" ->
                    let deserializedObj = JsonConvert.DeserializeObject<retweetRec>(data,settings)
                    let user = select ("/user/"+deserializedObj.userName) system
                    user <! ReTweet(deserializedObj.tweetId)

                | "DM" ->
                    let deserializedObj = JsonConvert.DeserializeObject<DMRec>(data,settings)
                    let user = select ("/user/"+ deserializedObj.sendTo) system
                    user <! DM(deserializedObj.message, deserializedObj.userName)
                | "Query" ->
                    let deserializedObj = JsonConvert.DeserializeObject<query>(data,settings)
                    let user = select ("/user/"+ deserializedObj.userName) system
                    user <! Query deserializedObj
                | _ -> printfn "This is unexpected"

            | RegisterSuccess response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | LoginSuccess response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | LogoutSuccess response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | FollowedSuccess response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | SubscriptionSuccess response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | TweetSuccess response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | QuerySuccess response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | AllTweets response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | TweetReceived response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore
            | DMSuccess response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore
            | ReTweetSuccess response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | ErrorResp response ->
                Async.StartImmediateAsTask((task webSocket context (JsonConvert.SerializeObject response))) |> ignore 
            | _ -> printfn "System messed up"
            return! loop()
        }
    loop()
let WSConn (webSocket : WebSocket) (context: Http.HttpContext) =
    let webActor = spawn system ("websocket" + string socketsSpawned) (WSActor webSocket context)
    socketsSpawned <- socketsSpawned + 1
    socket {
        let mutable loop = true

        while loop do
            let! msg = webSocket.read()
            match msg with
            | (Text, data, true) ->
                let str = UTF8.toString data
                let response = str
                webActor <! Request str
            | (Close, _, _) ->
                let emptyResponse = [||] |> ByteSegment
                do! webSocket.send Close emptyResponse true
                loop <- false

            | _ -> ()
    }




let app : WebPart =
    choose [
        path "/websocket" >=> handShake WSConn]

startWebServer defaultConfig app