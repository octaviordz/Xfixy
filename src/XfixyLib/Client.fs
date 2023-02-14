[<RequireQualifiedAccess>]
module internal Client

open System
open System.IO
open System.Collections
open System.Threading
open System.IO.Pipes
open Elmish

type RefAgent = int

type Model =
    { Message: Generic.KeyValuePair<string, string> list
      RefAgent: Option<RefAgent>
      CancellationToken: CancellationToken }

type Msg =
    | StartAgent
    | Send

[<AutoOpen>]
module internal Internal =
    let logger = Xfixy.Logging.mkLogger ()

    module Agent =
        type Model =
            { Outbound: string list
              RetryCount: int
              Inbound: string list
              CancellationToken: CancellationToken }
            static member Empty =
                { Outbound = []
                  RetryCount = 0
                  Inbound = []
                  CancellationToken = CancellationToken.None }

        type MailboxMsg =
            | Send of string list
            | FetchModel of AsyncReplyChannel<Model>

        type Msg = Send of Outbound: string list

        type Message =
            | Send
            | PutOutbound of string list
            | Retry

        let initWith updater =
            { Outbound = []
              RetryCount = 0
              Inbound = []
              CancellationToken = CancellationToken.None }
            |> updater

        let store = Generic.List<_>()

        let mkAndStartAgent init : RefAgent =
            let pipeClient = new NamedPipeClientStream(".", "Xfixy-pipe", PipeDirection.InOut)

            let mutable interactor =
                {| State = Model.Empty
                   Dispatch = Unchecked.defaultof<_> |}

            let setStateIntercept model dispatch =
                interactor <- {| State = model; Dispatch = dispatch |}
                ()

            let update (msg: Message) (model: Model) =
                let inline logExn (ex: exn) (message: string) =
                    logger.LogError(ex, """Error sending with message "{message}".""", message)

                    false

                match msg with
                | PutOutbound x ->
                    let model' = { model with Outbound = x }
                    model', Cmd.none
                | Send ->
                    let retryList = Generic.List<string>()

                    for message in model.Outbound do
                        logger.LogTrace(
                            """Sending message "{message}". RetryCount: {retryCount}""",
                            message,
                            model.RetryCount
                        )

                        if not pipeClient.IsConnected then
                            try
                                pipeClient.Connect(200)
                            with
                            | :? TimeoutException as ex -> logger.LogTrace(ex, "Timeout error.")

                        if pipeClient.IsConnected then
                            try
                                //use sr = new StreamReader(pipeClient)

                                let sw = new StreamWriter(pipeClient)
                                sw.AutoFlush <- true
                                // Send a 'message' and wait for client to receive it.
                                sw.WriteLine(message) //do! sw.WriteLineAsync(psResultAsString.AsMemory(), ct)

                                pipeClient.WaitForPipeDrain()
                            with
                            | :? IOException as ex ->
                                retryList.Add message
                                // Catch the IOException that is raised if the pipe is broken or disconnected.
                                logger.LogWarning(
                                    ex,
                                    """IOError calling shareResultAsync with message "{message}".""",
                                    message
                                )
                                // If broken reconnect.
                                pipeClient.Close()
                            | ex when (logExn ex message) -> ()

                    let model', cmd =
                        match retryList |> List.ofSeq with
                        | [] ->
                            { model with
                                RetryCount = 0
                                Outbound = [] },
                            Cmd.none
                        | list -> { model with Outbound = list }, Cmd.ofMsg Retry

                    model', cmd
                | Retry ->
                    let messageAsString = sprintf "%A" model.Outbound

                    logger.LogTrace(
                        """Retry sending message "{message}". RetryCount: {retryCount}""",
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

            let agent =
                MailboxProcessor<MailboxMsg>.Start
                    (fun inbox ->
                        let view _m _d = ignore

                        let program = Program.mkProgram init update view

                        let setState = (program |> Program.setState)

                        let setState' model dispatch =
                            setState model dispatch
                            setStateIntercept model dispatch

                        program
                        |> Program.withSetState setState'
                        |> Program.withErrorHandler (fun (error: string, ex: exn) -> logger.LogError(ex, error))
                        |> Program.run

                        // the message processing function
                        let rec messageLoop () =
                            async {
                                // read a message
                                let! msg = inbox.Receive()

                                match msg with
                                | MailboxMsg.Send x ->
                                    interactor.Dispatch(PutOutbound x)
                                    interactor.Dispatch Send
                                | MailboxMsg.FetchModel replyChannel -> replyChannel.Reply interactor.State

                                // loop to top
                                return! messageLoop ()
                            }
                        // start the loop
                        messageLoop ())

            store.Add agent
            (store.Count - 1)

        let post (refAgent: RefAgent) (msg: Msg) =
            let agent = store[refAgent]
            //let model = agent.PostAndReply(fun replyChannel -> FetchModel replyChannel)
            match msg with
            | Msg.Send x -> agent.Post(MailboxMsg.Send x)

            ()


// Initial model and Cmd for client app.
let init (arg: {| CancellationToken: CancellationToken |}) =

    { Message = []
      RefAgent = None
      CancellationToken = arg.CancellationToken },
    Cmd.ofMsg StartAgent


let update (msg: Msg) (model: Model) =
    match msg with
    | StartAgent ->
        let agentInit () =
            Agent.initWith (fun m -> { m with CancellationToken = model.CancellationToken }), Cmd.none
        // Side effect.
        let refAgent = Agent.mkAndStartAgent agentInit
        let model' = { model with RefAgent = Some refAgent }
        model', Cmd.none
    | Send ->
        let messages = Generic.List<string>()

        for kv in model.Message do
            let psResultAsString = kv.Value
            messages.Add kv.Value
            logger.LogTrace("""[CLIENT.clientUpdate] Queueing message "{message}".""", psResultAsString)

        match model.RefAgent with
        | Some refAgent ->
            let msg = Agent.Msg.Send(List.ofSeq messages)
            Agent.post refAgent msg
        | _ -> logger.LogError("[CLIENT.clientUpdate] Invalid operation. Must start agent first.")

        let model', cmd = { model with Message = [] }, Cmd.none
        model', cmd
