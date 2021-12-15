module DataDefnitions

type tweet = {Id: string; message: string; senderId: string; hashTags: string; mentions: string; asRetweet: bool}
type query = {searchOn: string; searchWith: string; userName: string}

type ErrorInfo = {Message: string; Heading: string}
type SimpleResponse = {Message: string; From: string; Heading: string}
type TweetResponse = {Message: tweet; Heading: string}
// type TweetReceivedResponse = {Message: tweet; Heading: string; From: string}
type ListResponse = {Results: seq<tweet>; Heading: string}
type DMResponse = {Message: string; From: string; Heading: string}

type justUser = {userName: string; password: string}
type subscriberRec = {userName: string; subscriberId: string}
type retweetRec = {tweetId: string; userName: string}
type DMRec = {userName: string; sendTo: string; message: string}

type Message = 
    | Login of (string * string)
    | Logout of (string * string)
    | Tweet of tweet
    | Query of query
    | Register of (string * string)
    | NewFollower of string
    | Subscribed of List<string>
    | TweetNotification of tweet
    | ReTweet of string
    | DM of (string * string)

type Commands = 
    | StartRegister
    | RegisterSuccess of SimpleResponse
    | LoginSuccess of SimpleResponse
    | LogoutSuccess of SimpleResponse
    | FollowedSuccess of SimpleResponse
    | SubscriptionSuccess of SimpleResponse
    | TweetSuccess of TweetResponse
    | TweetReceived of TweetResponse
    | ReTweetSuccess of TweetResponse
    | DMSuccess of DMResponse
    | QuerySuccess of ListResponse
    | AllTweets of ListResponse
    | Request of string
    | ErrorResp of ErrorInfo

type Coordinator = 
    | CreateUsers of string
    | FollowerNotification of (string * string)
    | TweetNotificationCarrer of (string * tweet)

type remoteIntitator = 
    | Trigger of string 
