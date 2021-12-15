#r "nuget: Suave"
#r "nuget: FSharp.Json"
#load "dataDefnitions.fs"

open System
open Suave
open System.Net.WebSockets
open System.Threading
open FSharp.Json
open DataDefnitions
open System.Text.RegularExpressions
open System.Security.Cryptography


let socket = new ClientWebSocket()
let cts = new CancellationTokenSource()
let uri = Uri("ws://localhost:8080/websocket")

let connection = socket.ConnectAsync(uri, cts.Token)

while not (connection.IsCompletedSuccessfully) do
    Thread.Sleep(100)

let ws(socket: WebSocket) = 
    async {
        while true do
            let rcvBytes: byte [] = Array.zeroCreate 1280
            let rcvBuffer = new ArraySegment<byte>(rcvBytes) 
            let! res = (socket.ReceiveAsync(rcvBuffer, cts.Token))
            let responseString = (UTF8.toString (rcvBuffer.ToArray())).Trim([|' '; (char) 0|])
            printfn "%A" responseString
            // printfn "userIsLive>"
    }

Async.StartImmediateAsTask(ws socket)

let processTweet(message) = 
    let mutable msg = message
    let hashTags = (" ", Regex(@"#\w+").Matches message) |> String.Join
    let mentions = (" ", Regex(@"@\w+").Matches message) |> String.Join
    (hashTags, mentions)

let constructHash arg1=
    let byteInfo = System.Text.Encoding.ASCII.GetBytes(arg1: string)
    (new SHA256Managed()).ComputeHash(byteInfo) |> BitConverter.ToString |> fun s -> s.Replace("-", "")

let getBytes data requestAPI requestType = 
    let header = "RequestType: " + requestType + "\n" + "RequestAPI: " + requestAPI + "\n" + "Body: "
    let fullJson = header + Json.serialize data
    fullJson
    |> System.Text.Encoding.ASCII.GetBytes
    |> ArraySegment

printfn "Please enter a valid command like 'register', 'login', 'logout', 'subscribe', 'tweet', 'retweet', 'dm'"
let mutable activeUser = ""
let mutable hashPwd = ""
while true do
    let action = Console.ReadLine()
    match action with
    | "register" ->
        printfn "Enter UserName"
        let username = Console.ReadLine()
        activeUser <- username
        printfn "Enter Password"
        let password = Console.ReadLine()
        hashPwd <- password
        let data = { userName = username; password = password }
        let bytes = getBytes data "Register" "Post"
        socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token) |> ignore
    | "login" -> 
        printfn "Enter UserName"
        let uname = Console.ReadLine()
        printfn "Enter Password"
        let pwd = Console.ReadLine()
        let data = { userName = uname; password = pwd }
        let bytes = getBytes data "Login" "Post"
        socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token) |> ignore
    | "subscribe" -> 
        printfn "Subscribe to"
        let subscribeTo = Console.ReadLine()
        let data = { userName = activeUser; subscriberId = subscribeTo }
        let bytes = getBytes data "Subscribe" "Post"
        socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token) |> ignore
    | "tweet" -> 
        printfn "Tweet"
        let message = Console.ReadLine()
        let Id = DateTime.Now.Ticks |> string
        let processedData = processTweet(message)
        let data = { Id = Id; message = message; senderId = activeUser; hashTags = fst processedData; mentions = snd processedData; asRetweet=false}
        let bytes = getBytes data "Tweet" "Post"
        socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token) |> ignore
    | "retweet" -> 
        printfn "ReTweet"
        let tweetId = Console.ReadLine()
        let data = { tweetId = tweetId; userName = activeUser }
        let bytes = getBytes data "ReTweet" "Post"
        socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token) |> ignore
    | "dm" -> 
        printfn "Enter the message"
        let message = Console.ReadLine()
        printfn "Enter the recipient ID"
        let sendTo = Console.ReadLine()
        let data = { sendTo = sendTo; userName = activeUser; message = message }
        let bytes = getBytes data "DM" "Post"
        socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token) |> ignore
    | "query" -> 
        printfn "Query to be performed on"
        let searchOn = Console.ReadLine()
        printfn "Search pattern"
        let searchWith = Console.ReadLine()
        let data = {userName = activeUser; searchOn = searchOn; searchWith = searchWith}
        let bytes = getBytes data "Query" "Post"
        socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token) |> ignore
    | "logout" -> 
        printfn "You are successfully logged out"
        let data = { userName = activeUser; password = "" }
        let bytes = getBytes data "Logout" "Post"
        socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token) |> ignore
    | _ ->
        printfn "Bad Input please pick a valid action"


System.Console.ReadLine() |> ignore
