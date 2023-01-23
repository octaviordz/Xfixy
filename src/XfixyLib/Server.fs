namespace Xfixy.Server

open Xfixy
open System
open System.IO
open System.Threading
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open System.IO.Pipes

type Worker(logger: ILogger<Worker>, configuration: IConfiguration) =
    inherit BackgroundService()

    let pipeServer: NamedPipeServerStream =
        new NamedPipeServerStream("Xfixy-pipe", PipeDirection.InOut)

    let messager = Messager()

    member self.OnMessage
        with public get (): IObservable<string> = messager

    override self.ExecuteAsync(ct: CancellationToken) =
        task {
            while not ct.IsCancellationRequested do
                try
                    logger.LogInformation("[SERVER] Worker running at: {time}.", DateTimeOffset.Now)
                    do! pipeServer.WaitForConnectionAsync()
                    use sr = new StreamReader(pipeServer)
                    logger.LogTrace("[SERVER] Current TransmissionMode: {0}.", pipeServer.TransmissionMode)

                    let rec loop () =
                        task {
                            let! line = sr.ReadLineAsync ct

                            if not (isNull line) then
                                messager.Push line

                            logger.LogInformation("[SERVER] Echo: " + line)

                            if isNull line then
                                return Unchecked.defaultof<_>
                            else
                                return! loop ()
                        }
                    do! loop ()
                    //let mutable continueLoop = true
                    //while (not ct.IsCancellationRequested) && continueLoop do
                    //    let! line = sr.ReadLineAsync ct
                    //    if not (isNull line) then
                    //        messager.Push line
                    //    continueLoop <- not (isNull line)
                    //    logger.LogInformation("[SERVER] Echo: " + line)
                with
                | _ as ex -> logger.LogError(ex, "[SERVER] Error.")
        }
