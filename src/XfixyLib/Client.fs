[<RequireQualifiedAccess>]
module internal Client

open System
open System.IO
open System.Collections
open System.Threading
open Microsoft.Extensions.Logging
open System.IO.Pipes
open Elmish

type Model =
    { Message: Generic.KeyValuePair<string, string> list
      RetryCount: int
      Client: NamedPipeClientStream
      Logger: ILogger
      CancellationToken: CancellationToken }

type Msg =
    | Send
    | Retry

// Initial model and Cmd for client app.
let init
    (arg: {| Logger: ILogger
             CancellationToken: CancellationToken |})
    =
    { Message = []
      RetryCount = 0
      Client = new NamedPipeClientStream(".", "Xfixy-pipe", PipeDirection.InOut)
      Logger = arg.Logger
      CancellationToken = arg.CancellationToken },
    Cmd.none

let update (msg: Msg) (model: Model) =
    let logger = model.Logger
    let ct = model.CancellationToken
    
    let inline logExn (ex: exn) (psResultAsString: string) =
        logger.LogError(
            ex,
            """[CLIENT.clientUpdate] Error sending with message "{message}".""",
            psResultAsString
        )

        false

    match msg with
    | Send ->
        let mutable pipeClient' = Option<NamedPipeClientStream>.None
        let retryList = Generic.List<Generic.KeyValuePair<string, string>>()
        for kv in model.Message do
            let psResultAsString = kv.Value

            logger.LogTrace(
                """[CLIENT.clientUpdate] Sending message "{message}". RetryCount: {retryCount}""",
                psResultAsString,
                model.RetryCount
            )

            let pipeClient = model.Client
            // for kv in scriptContentDict do
            //     let res = scriptContentDict[kv.Key]
            //     match res with
            //     | Result.Ok psResultAsString ->
            //         logger.LogInformation("""PowerShell Result: "{psResultAsString}".""", psResultAsString)
            //         try
            //             do! shareResultAsync psResultAsString ct
            //         with
            //         | ex when not (ex :? OperationCanceledException) ->
            //             logger.LogError(
            //                 """Error calling shareResult with value: "{psResultAsString}".""",
            //                 psResultAsString
            //             )
            //     | Result.Error err -> logger.LogError(err)
            if not pipeClient.IsConnected then
                try
                    pipeClient.Connect(200) //do! pipeClient.ConnectAsync(200, ct)
                with
                | :? TimeoutException as ex -> logger.LogTrace(ex, "[CLIENT.clientUpdate] Timeout error.")

            if pipeClient.IsConnected then
                try
                    let sw = new StreamWriter(pipeClient)
                    sw.AutoFlush <- true
                    // Send a 'message' and wait for client to receive it.
                    sw.WriteLine(psResultAsString) //do! sw.WriteLineAsync(psResultAsString.AsMemory(), ct)

                    pipeClient.WaitForPipeDrain()
                with
                | :? IOException as ex ->
                    retryList.Add kv
                    // Catch the IOException that is raised if the pipe is broken or disconnected.
                    logger.LogWarning(
                        ex,
                        """[CLIENT.clientUpdate] IOError calling shareResultAsync with message "{message}".""",
                        psResultAsString
                    )
                    // If broken create a new pipe.
                    pipeClient.Close()
                    pipeClient.Dispose()
                    pipeClient' <- Some(new NamedPipeClientStream(".", "Xfixy-pipe", PipeDirection.InOut))
                | ex when (logExn ex psResultAsString) -> ()
        //let consume () = consumeScriptsResultAsync model'
        //model', Cmd.batch [ cmd Cmd.OfTask.perform consume () id ]
        let model', cmd =
            match pipeClient' with
            | Some c -> { model with Client = c; Message = List.ofSeq retryList }, Cmd.ofMsg Retry
            | _ -> { model with RetryCount = 0; Message = [] }, Cmd.none

        model', cmd
    | Retry ->
        let messageAsString = sprintf "%A" model.Message
        logger.LogInformation(
            """[CLIENT.clientUpdate] Retry sending message "{message}". RetryCount: {retryCount}""",
            messageAsString,
            model.RetryCount
        )

        match model.RetryCount with
        | x when (x > 2) ->
            let model' = { model with RetryCount = 0 }
            model', Cmd.none
        | _ ->
            let model' = { model with RetryCount = model.RetryCount + 1 }
            model', Cmd.ofMsg Send
