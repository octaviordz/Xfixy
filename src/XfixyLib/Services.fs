﻿namespace Xfixy.Services

open Xfixy
open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open System.Collections
open Elmish

[<AutoOpen>]
module internal Internal =
    type InternalLogLevel = Xfixy.Logging.LogLevel

    let setLogger (logger: ILogger) =
        Xfixy.Logging.setLogFunc (fun logLevel msgFunc ex msgParameters ->
            match logLevel with
            | InternalLogLevel.Trace -> logger.Log(LogLevel.Trace, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Debug -> logger.Log(LogLevel.Debug, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Info -> logger.Log(LogLevel.Information, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Warn -> logger.Log(LogLevel.Warning, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Error -> logger.Log(LogLevel.Error, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Fatal -> logger.Log(LogLevel.Critical, ex, msgFunc.Invoke(), msgParameters)
            | _ -> ()

            ())

module internal Control =
    open Xfixy.Control

    type ScriptsConfig = { Location: string }

    [<RequireQualifiedAccess>]
    type StatusMessage =
        | None
        | Ok of ResultValue: string
        | Error of ErrorValue: string

    type FetchScriptsResult = Option<Result<unit, string>>

    module FetchScriptsResult =
        let Ok: FetchScriptsResult = Some(Result<unit, string>.Ok ())
        let Error error : FetchScriptsResult = Some(Result<unit, string>.Error error)

    type WorkerModel =
        { StatusMessage: StatusMessage
          LastFetchScriptsResult: FetchScriptsResult
          ScriptDict: Generic.IDictionary<string, string>
          ScriptResultDict: Deferred<Generic.IDictionary<string, Result<string, string>>>
          ScriptsConfig: ScriptsConfig
          CancellationToken: CancellationToken }

    type Model = WorkerModel * Client.Model

    type ContentDict = Generic.IDictionary<string, string>
    type ResultDict = Generic.IDictionary<string, Result<string, string>>

    type WorkerMsg =
        | FetchScripts of AsyncOperationStatus<Result<ContentDict, exn>>
        | ExecuteScripts of AsyncOperationStatus<Result<ResultDict, exn>>
        | ConsumeScriptsCompleted

    type Msg =
        | WorkerMsg of WorkerMsg
        | ClientMsg of Client.Msg

    type internal FetchScript =
        | Fetch
        | Not

    [<RequireQualifiedAccess>]
    type internal SubscriptionKind =
        | FetchScripts
        | ExecuteScripts

    let fetchScriptsAsync location ct =
        task {
            let scriptFiles = Directory.GetFiles(location, "*.ps1")
            let! res = PSscript.loadScriptsAsync scriptFiles ct
            return FetchScripts(Finished(Ok res))
        }

    let runScriptsAync scriptContentDict ct =
        task {
            let! resDict = runScriptAsync scriptContentDict ct
            return ExecuteScripts(Finished(Ok resDict))
        }

    let updateWith toMsg model (first, subCmd) =
        let _, b = model
        let model = (first, b)
        (model, Cmd.map toMsg subCmd)

    let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
        let workerModel, clientModel = model

        match msg with
        | ClientMsg clientMsg ->
            let clientModel', clientCmd' = Client.update clientMsg clientModel
            let model' = (workerModel, clientModel')
            (model', Cmd.map ClientMsg clientCmd')
        | WorkerMsg msg ->
            match msg with
            | FetchScripts Started ->
                let location = workerModel.ScriptsConfig.Location
                let ct = workerModel.CancellationToken

                if not (Directory.Exists location) then
                    let ex = DirectoryNotFoundException(location)
                    let cmd = Cmd.ofMsg (FetchScripts(Finished(Error ex)))

                    (workerModel, cmd) |> updateWith WorkerMsg model
                else
                    let fetch location = fetchScriptsAsync location ct

                    let cmd =
                        Cmd.OfTask.either fetch location id (fun ex -> FetchScripts(Finished(Error ex)))

                    (workerModel, cmd) |> updateWith WorkerMsg model
            | FetchScripts (Finished (Error ex)) ->
                let model' =
                    { workerModel with
                        LastFetchScriptsResult = FetchScriptsResult.Error $"ScriptsPath not found: {ex.Message}" }

                (model', Cmd.none) |> updateWith WorkerMsg model
            | FetchScripts (Finished (Ok contentDict)) ->
                let model' =
                    { workerModel with
                        ScriptDict = contentDict
                        LastFetchScriptsResult = FetchScriptsResult.Ok }

                (model', Cmd.none) |> updateWith WorkerMsg model
            | ExecuteScripts Started ->
                let scriptContentDict = workerModel.ScriptDict
                let ct = workerModel.CancellationToken
                let execute scriptContentDict = runScriptsAync scriptContentDict ct
                let model' = { workerModel with ScriptResultDict = InProgress }

                let cmd =
                    Cmd.OfTask.either execute scriptContentDict id (fun ex -> ExecuteScripts(Finished(Error ex)))

                (model', cmd) |> updateWith WorkerMsg model
            | ExecuteScripts (Finished (Error ex)) ->
                let model' =
                    { workerModel with StatusMessage = StatusMessage.Error $"Error executing script. {ex.Message}" }

                (model', Cmd.none) |> updateWith WorkerMsg model
            | ExecuteScripts (Finished (Ok scriptContentDict)) ->
                let workerModel' =
                    { workerModel with ScriptResultDict = Resolved scriptContentDict }

                match scriptContentDict with
                | dict when dict.Count > 0 ->
                    //let message =
                    //    seq { for x in 1 .. n do if x%2=0 then yield x }
                    let message =
                        seq {
                            for k in dict.Keys do
                                let res = dict[k]

                                let v =
                                    match dict[k] with
                                    | Ok v -> v
                                    | Error err -> err

                                yield Generic.KeyValuePair(k, v)
                        }
                        |> List.ofSeq

                    let clientModel' = { clientModel with Message = message }
                    let completeModel = (workerModel', clientModel')
                    let msg = ClientMsg Client.Msg.Send
                    (completeModel, Cmd.ofMsg msg)
                | _ ->
                    let completeModel = (workerModel', clientModel)
                    (completeModel, Cmd.none)
            | ConsumeScriptsCompleted ->
                (workerModel, Cmd.none)
                |> updateWith WorkerMsg model
// https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
// New-Service -Name Xfinixy -BinaryPathName C:\Users\...\Xfixy.exe
open Control

type Worker(logger: ILogger<Worker>, configuration: IConfiguration) =
    inherit BackgroundService()
    do setLogger logger

    override self.ExecuteAsync(ct: CancellationToken) =
        task {
            logger.LogInformation("Worker starting at: {time}.", DateTimeOffset.Now)

            let scriptReloadInterval () =
                let v = configuration.GetValue<int>("Worker:Script:ReloadIntervalSeconds", 15)
                v

            let delay () =
                let v = configuration.GetValue<int>("Worker:Delay", 4000)
                v

            let scriptsPath () =
                let v = configuration.GetValue<string>("Worker:Scripts-Location", String.Empty)
                v

            let scriptsLocation = scriptsPath ()

            // Initial model and Cmd.
            let init (arg) : Model * Cmd<Msg> =
                let workerModel =
                    { StatusMessage = StatusMessage.None
                      LastFetchScriptsResult = FetchScriptsResult.None
                      ScriptDict = Generic.Dictionary<string, string>()
                      ScriptResultDict = HasNotStartedYet
                      ScriptsConfig = { Location = scriptsLocation }
                      CancellationToken = ct }

                let clientInit = Client.init {| CancellationToken = ct |}

                let clientModel, clientCmd = clientInit
                let msg = (FetchScripts Started) |> WorkerMsg

                let cmd =
                    Cmd.batch [ Cmd.ofMsg msg
                                (clientCmd |> Cmd.map (fun it -> it |> ClientMsg)) ]

                let model = (workerModel, clientModel)
                (model, cmd)

            let view _model _dispatch = ignore

            let subscriptionObservers = Generic.List<IObserver<SubscriptionKind list>>()

            let subscriptionObservable =
                { new IObservable<SubscriptionKind list> with
                    member _.Subscribe(observer) =
                        if not (subscriptionObservers.Contains observer) then
                            subscriptionObservers.Add observer

                        new Unsubscriber<SubscriptionKind list>(subscriptionObservers, observer) }

            let triggerSubscription kindList =
                for observer in subscriptionObservers do
                    observer.OnNext(kindList)
            // https://elmish.github.io/elmish/docs/subscription.html#migrating-from-v3
            let subscription (model: Model) : (SubId * Subscribe<Msg>) list =
                let execSubscription dispatch : IDisposable =
                    subscriptionObservable
                    |> Observable.subscribe (fun kList ->
                        kList
                        |> List.map (fun it ->
                            match it with
                            | SubscriptionKind.FetchScripts -> WorkerMsg(FetchScripts Started)
                            | SubscriptionKind.ExecuteScripts -> WorkerMsg(ExecuteScripts Started))
                        |> List.iter (fun msg -> dispatch msg))

                [ [ nameof execSubscription ], execSubscription ]

            Program.mkProgram init update view
            |> Program.withErrorHandler (fun (error: string, ex: exn) -> logger.LogError(ex, error))
            |> Program.withSubscription subscription
            |> Program.run
            ///////////////////////////
            let stopWatch = Stopwatch()

            while not ct.IsCancellationRequested do
                do! Task.Delay(delay (), ct)
                logger.LogInformation("Worker running at: {time}.", DateTimeOffset.Now)

                let fetchOrNot =
                    if not stopWatch.IsRunning then
                        stopWatch.Start()
                        Not
                    elif stopWatch.Elapsed.TotalSeconds
                         >= scriptReloadInterval () then
                        Fetch
                    else
                        Not

                match fetchOrNot with
                | Fetch ->
                    stopWatch.Restart()

                    [ SubscriptionKind.FetchScripts
                      SubscriptionKind.ExecuteScripts ]
                    |> triggerSubscription
                | Not ->
                    [ SubscriptionKind.ExecuteScripts ]
                    |> triggerSubscription
        }
