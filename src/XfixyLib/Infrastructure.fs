namespace Xfixy.Infrastructure
namespace Elmish

(**
Log
---------
Basic cross-platform logging API.

*)
module internal Log =

#if FABLE_COMPILER
    open Fable.Core.JS

    let onError (text: string, ex: exn) = console.error (text,ex)
    let toConsole(text: string, o: #obj) = console.log(text,o)

#else
#if NETSTANDARD2_0
    let onError (text: string, ex: exn) = System.Diagnostics.Trace.TraceError("{0}: {1}", text, ex)
    let toConsole(text: string, o: #obj) = printfn "%s: %A" text o
#else
    let onError (text: string, ex: exn) = System.Console.Error.WriteLine("{0}: {1}", text, ex)
    let toConsole(text: string, o: #obj) = printfn "%s: %A" text o
#endif
#endif

#if FABLE_COMPILER
module internal Timer =
    open System.Timers
    let delay interval callback =
        let t = new Timer(float interval, AutoReset = false)
        t.Elapsed.Add callback
        t.Enabled <- true
        t.Start()
#endif


namespace Elmish
open System

[<Struct>]
type internal RingState<'item> =
    | Writable of wx:'item array * ix:int
    | ReadWritable of rw:'item array * wix:int * rix:int

type internal RingBuffer<'item>(size) =
    let doubleSize ix (items: 'item array) =
        seq { yield! items |> Seq.skip ix
              yield! items |> Seq.take ix
              for _ in 0..items.Length do
                yield Unchecked.defaultof<'item> }
        |> Array.ofSeq

    let mutable state : 'item RingState =
        Writable (Array.zeroCreate (max size 10), 0)

    member __.Pop() =
        match state with
        | ReadWritable (items, wix, rix) ->
            let rix' = (rix + 1) % items.Length
            match rix' = wix with
            | true ->
                state <- Writable(items, wix)
            | _ ->
                state <- ReadWritable(items, wix, rix')
            Some items.[rix]
        | _ ->
            None

    member __.Push (item:'item) =
        match state with
        | Writable (items, ix) ->
            items.[ix] <- item
            let wix = (ix + 1) % items.Length
            state <- ReadWritable(items, wix, ix)
        | ReadWritable (items, wix, rix) ->
            items.[wix] <- item
            let wix' = (wix + 1) % items.Length
            match wix' = rix with
            | true ->
                state <- ReadWritable(items |> doubleSize rix, items.Length, 0)
            | _ ->
                state <- ReadWritable(items, wix', rix)


(**
Cmd
---------
Core abstractions for dispatching messages in Elmish.

*)

namespace Elmish

open System

/// Dispatch - feed new message into the processing loop
type Dispatch<'msg> = 'msg -> unit

/// Effect - return immediately, but may schedule dispatch of a message at any time
type Effect<'msg> = Dispatch<'msg> -> unit

/// Cmd - container for effects that may produce messages
type Cmd<'msg> = Effect<'msg> list

/// Cmd module for creating and manipulating commands
[<RequireQualifiedAccess>]
module Cmd =
    /// Execute the commands using the supplied dispatcher
    let internal exec onError (dispatch: Dispatch<'msg>) (cmd: Cmd<'msg>) =
        cmd |> List.iter (fun call -> try call dispatch with ex -> onError ex)

    /// None - no commands, also known as `[]`
    let none : Cmd<'msg> =
        []

    /// When emitting the message, map to another type
    let map (f: 'a -> 'msg) (cmd: Cmd<'a>) : Cmd<'msg> =
        cmd |> List.map (fun g -> (fun dispatch -> f >> dispatch) >> g)

    /// Aggregate multiple commands
    let batch (cmds: #seq<Cmd<'msg>>) : Cmd<'msg> =
        cmds |> List.concat

    /// Command to call the effect
    let ofEffect (effect: Effect<'msg>) : Cmd<'msg> =
        [effect]

    module OfFunc =
        /// Command to evaluate a simple function and map the result
        /// into success or error (of exception)
        let either (task: 'a -> _) (arg: 'a) (ofSuccess: _ -> 'msg) (ofError: _ -> 'msg) : Cmd<'msg> =
            let bind dispatch =
                try
                    task arg
                    |> (ofSuccess >> dispatch)
                with x ->
                    x |> (ofError >> dispatch)
            [bind]

        /// Command to evaluate a simple function and map the success to a message
        /// discarding any possible error
        let perform (task: 'a -> _) (arg: 'a) (ofSuccess: _ -> 'msg) : Cmd<'msg> =
            let bind dispatch =
                try
                    task arg
                    |> (ofSuccess >> dispatch)
                with x ->
                    ()
            [bind]

        /// Command to evaluate a simple function and map the error (in case of exception)
        let attempt (task: 'a -> unit) (arg: 'a) (ofError: _ -> 'msg) : Cmd<'msg> =
            let bind dispatch =
                try
                    task arg
                with x ->
                    x |> (ofError >> dispatch)
            [bind]

    module OfAsyncWith =
        /// Command that will evaluate an async block and map the result
        /// into success or error (of exception)
        let either (start: Async<unit> -> unit)
                   (task: 'a -> Async<_>)
                   (arg: 'a)
                   (ofSuccess: _ -> 'msg)
                   (ofError: _ -> 'msg) : Cmd<'msg> =
            let bind dispatch =
                async {
                    let! r = task arg |> Async.Catch
                    dispatch (match r with
                             | Choice1Of2 x -> ofSuccess x
                             | Choice2Of2 x -> ofError x)
                }
            [bind >> start]

        /// Command that will evaluate an async block and map the success
        let perform (start: Async<unit> -> unit)
                    (task: 'a -> Async<_>)
                    (arg: 'a)
                    (ofSuccess: _ -> 'msg) : Cmd<'msg> =
            let bind dispatch =
                async {
                    let! r = task arg |> Async.Catch
                    match r with
                    | Choice1Of2 x -> dispatch (ofSuccess x)
                    | _ -> ()
                }
            [bind >> start]

        /// Command that will evaluate an async block and map the error (of exception)
        let attempt (start: Async<unit> -> unit)
                    (task: 'a -> Async<_>)
                    (arg: 'a)
                    (ofError: _ -> 'msg) : Cmd<'msg> =
            let bind dispatch =
                async {
                    let! r = task arg |> Async.Catch
                    match r with
                    | Choice2Of2 x -> dispatch (ofError x)
                    | _ -> ()
                }
            [bind >> start]

    module OfAsync =
#if FABLE_COMPILER
        let start x = Timer.delay 1 (fun _ -> Async.StartImmediate x)
#else
        let inline start x = Async.Start x
#endif
        /// Command that will evaluate an async block and map the result
        /// into success or error (of exception)
        let inline either (task: 'a -> Async<_>)
                          (arg: 'a)
                          (ofSuccess: _ -> 'msg)
                          (ofError: _ -> 'msg) : Cmd<'msg> =
            OfAsyncWith.either start task arg ofSuccess ofError

        /// Command that will evaluate an async block and map the success
        let inline perform (task: 'a -> Async<_>)
                           (arg: 'a)
                           (ofSuccess: _ -> 'msg) : Cmd<'msg> =
            OfAsyncWith.perform start task arg ofSuccess

        /// Command that will evaluate an async block and map the error (of exception)
        let inline attempt (task: 'a -> Async<_>)
                           (arg: 'a)
                           (ofError: _ -> 'msg) : Cmd<'msg> =
            OfAsyncWith.attempt start task arg ofError

    module OfAsyncImmediate =
        /// Command that will evaluate an async block and map the result
        /// into success or error (of exception)
        let inline either (task: 'a -> Async<_>)
                          (arg: 'a)
                          (ofSuccess: _ -> 'msg)
                          (ofError: _ -> 'msg) : Cmd<'msg> =
            OfAsyncWith.either Async.StartImmediate task arg ofSuccess ofError

        /// Command that will evaluate an async block and map the success
        let inline perform (task: 'a -> Async<_>)
                           (arg: 'a)
                           (ofSuccess: _ -> 'msg) : Cmd<'msg> =
            OfAsyncWith.perform Async.StartImmediate task arg ofSuccess

        /// Command that will evaluate an async block and map the error (of exception)
        let inline attempt (task: 'a -> Async<_>)
                           (arg: 'a)
                           (ofError: _ -> 'msg) : Cmd<'msg> =
            OfAsyncWith.attempt Async.StartImmediate task arg ofError

#if FABLE_COMPILER
    module OfPromise =
        /// Command to call `promise` block and map the results
        let either (task: 'a -> Fable.Core.JS.Promise<_>)
                   (arg:'a)
                   (ofSuccess: _ -> 'msg)
                   (ofError: #exn -> 'msg) : Cmd<'msg> =
            let bind dispatch =
                (task arg)
                    .``then``(ofSuccess >> dispatch)
                    .catch(unbox >> ofError >> dispatch)
                    |> ignore
            [bind]

        /// Command to call `promise` block and map the success
        let perform (task: 'a -> Fable.Core.JS.Promise<_>)
                   (arg:'a)
                   (ofSuccess: _ -> 'msg) =
            let bind dispatch =
                (task arg)
                    .``then``(ofSuccess >> dispatch)
                    |> ignore
            [bind]

        /// Command to call `promise` block and map the error
        let attempt (task: 'a -> Fable.Core.JS.Promise<_>)
                    (arg:'a)
                    (ofError: #exn -> 'msg) : Cmd<'msg> =
            let bind dispatch =
                (task arg)
                    .catch(unbox >> ofError >> dispatch)
                    |> ignore
            [bind]
#else
    open System.Threading.Tasks
    module OfTask =
        /// Command to call a task and map the results
        let inline either (task: 'a -> Task<_>)
                          (arg:'a)
                          (ofSuccess: _ -> 'msg)
                          (ofError: _ -> 'msg) : Cmd<'msg> =
            OfAsync.either (task >> Async.AwaitTask) arg ofSuccess ofError

        /// Command to call a task and map the success
        let inline perform (task: 'a -> Task<_>)
                           (arg:'a)
                           (ofSuccess: _ -> 'msg) : Cmd<'msg> =
            OfAsync.perform (task >> Async.AwaitTask) arg ofSuccess

        /// Command to call a task and map the error
        let inline attempt (task: 'a -> Task<_>)
                           (arg:'a)
                           (ofError: _ -> 'msg) : Cmd<'msg> =
            OfAsync.attempt (task >> Async.AwaitTask) arg ofError
#endif

    /// Command to issue a specific message
    let inline ofMsg (msg:'msg) : Cmd<'msg> =
        [fun dispatch -> dispatch msg]


namespace Elmish

open System

/// SubId - Subscription ID, alias for string list
type SubId = string list

/// Subscribe - Starts a subscription, returns IDisposable to stop it
type Subscribe<'msg> = Dispatch<'msg> -> IDisposable

/// Subscription - Generates new messages when running
type Sub<'msg> = (SubId * Subscribe<'msg>) list

module Sub =

    /// None - no subscriptions, also known as `[]`
    let none : Sub<'msg> =
        []

    /// Aggregate multiple subscriptions
    let batch (subs: #seq<Sub<'msg>>) : Sub<'msg> =
        subs |> List.concat

    /// When emitting the message, map to another type.
    /// To avoid ID conflicts with other components, scope SubIds with a prefix.
    let map (idPrefix: string) (f: 'a -> 'msg) (sub: Sub<'a>) : Sub<'msg> =
        sub |> List.map (fun (subId, subscribe) ->
            idPrefix :: subId,
            fun dispatch -> subscribe (f >> dispatch))

    module Internal =

        module SubId =

            let toString (subId: SubId) =
                String.Join("/", subId)

        module Fx =

            let warnDupe onError subId =
                let ex = exn "Duplicate SubId"
                onError ("Duplicate SubId: " + SubId.toString subId, ex)

            let tryStop onError (subId, sub: IDisposable) =
                try
                    sub.Dispose()
                with ex ->
                    onError ("Error stopping subscription: " + SubId.toString subId, ex)

            let tryStart onError dispatch (subId, start) : (SubId * IDisposable) option =
                try
                    Some (subId, start dispatch)
                with ex ->
                    onError ("Error starting subscription: " + SubId.toString subId, ex)
                    None

            let stop onError subs =
                subs |> List.iter (tryStop onError)

            let change onError dispatch (dupes, toStop, toKeep, toStart) =
                dupes |> List.iter (warnDupe onError)
                toStop |> List.iter (tryStop onError)
                let started = toStart |> List.choose (tryStart onError dispatch)
                List.append toKeep started

        module NewSubs =

            let (_dupes, _newKeys, _newSubs) as init =
                List.empty, Set.empty, List.empty

            let update (subId, start) (dupes, newKeys, newSubs) =
                if Set.contains subId newKeys then
                    subId :: dupes, newKeys, newSubs
                else
                    dupes, Set.add subId newKeys, (subId, start) :: newSubs

            let calculate subs =
                List.foldBack update subs init

        let empty = List.empty<SubId * IDisposable>

        let diff (activeSubs: (SubId * IDisposable) list) (sub: Sub<'msg>) =
            let keys = activeSubs |> List.map fst |> Set.ofList
            let dupes, newKeys, newSubs = NewSubs.calculate sub
            if keys = newKeys then
                dupes, [], activeSubs, []
            else
                let toKeep, toStop = activeSubs |> List.partition (fun (k, _) -> Set.contains k newKeys)
                let toStart = newSubs |> List.filter (fun (k, _) -> not (Set.contains k keys))
                dupes, toStop, toKeep, toStart

(**
Program
---------
Core abstractions for creating and running the dispatch loop.

*)

namespace Elmish


/// Program type captures various aspects of program behavior
type Program<'arg, 'model, 'msg, 'view> = private {
    init : 'arg -> 'model * Cmd<'msg>
    update : 'msg -> 'model -> 'model * Cmd<'msg>
    subscribe : 'model -> Sub<'msg>
    view : 'model -> Dispatch<'msg> -> 'view
    setState : 'model -> Dispatch<'msg> -> unit
    onError : (string*exn) -> unit
    termination : ('msg -> bool) * ('model -> unit)
}

/// Program module - functions to manipulate program instances
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Program =
    /// Typical program, new commands are produced by `init` and `update` along with the new state.
    let mkProgram
        (init : 'arg -> 'model * Cmd<'msg>)
        (update : 'msg -> 'model -> 'model * Cmd<'msg>)
        (view : 'model -> Dispatch<'msg> -> 'view) =
        { init = init
          update = update
          view = view
          setState = fun model -> view model >> ignore
          subscribe = fun _ -> Sub.none
          onError = Log.onError
          termination = (fun _ -> false), ignore }

    /// Simple program that produces only new state with `init` and `update`.
    let mkSimple
        (init : 'arg -> 'model)
        (update : 'msg -> 'model -> 'model)
        (view : 'model -> Dispatch<'msg> -> 'view) =
        { init = init >> fun state -> state, Cmd.none
          update = fun msg -> update msg >> fun state -> state, Cmd.none
          view = view
          setState = fun model -> view model >> ignore
          subscribe = fun _ -> Sub.none
          onError = Log.onError
          termination = (fun _ -> false), ignore }

    /// Subscribe to external source of events, overrides existing subscription.
    /// Return the subscriptions that should be active based on the current model.
    /// Subscriptions will be started or stopped automatically to match.
    let withSubscription (subscribe : 'model -> Sub<'msg>) (program: Program<'arg, 'model, 'msg, 'view>) =
        { program with
            subscribe = subscribe }

    /// Map existing subscription to external source of events.
    let mapSubscription map (program: Program<'arg, 'model, 'msg, 'view>) =
        { program with
            subscribe = map program.subscribe }

    /// Trace all the updates to the console
    let withConsoleTrace (program: Program<'arg, 'model, 'msg, 'view>) =
        let traceInit (arg:'arg) =
            let initModel,cmd = program.init arg
            Log.toConsole ("Initial state:", initModel)
            initModel,cmd

        let traceUpdate msg model =
            Log.toConsole ("New message:", msg)
            let newModel,cmd = program.update msg model
            Log.toConsole ("Updated state:", newModel)
            newModel,cmd

        let traceSubscribe model =
            let sub = program.subscribe model
            Log.toConsole ("Updated subs:", sub |> List.map fst)
            sub

        { program with
            init = traceInit
            update = traceUpdate
            subscribe = traceSubscribe }

    /// Trace all the messages as they update the model and subscriptions
    let withTrace trace (program: Program<'arg, 'model, 'msg, 'view>) =
        let update msg model =
            let state,cmd = program.update msg model
            let subIds = program.subscribe state |> List.map fst
            trace msg state subIds
            state,cmd
        { program with
            update = update }

    /// Handle dispatch loop exceptions
    let withErrorHandler onError (program: Program<'arg, 'model, 'msg, 'view>) =
        { program with
            onError = onError }

    /// Exit criteria and the handler, overrides existing.
    let withTermination (predicate: 'msg -> bool) (terminate: 'model -> unit) (program: Program<'arg, 'model, 'msg, 'view>) =
        { program with
            termination = predicate, terminate }

    /// Map existing criteria and the handler.
    let mapTermination map (program: Program<'arg, 'model, 'msg, 'view>) =
        { program with
            termination = map program.termination }

    /// Map existing error handler and return new `Program`
    let mapErrorHandler map (program: Program<'arg, 'model, 'msg, 'view>) =
        { program with
            onError = map program.onError }

    /// Get the current error handler
    let onError (program: Program<'arg, 'model, 'msg, 'view>) =
        program.onError

    /// Function to render the view with the latest state
    let withSetState (setState:'model -> Dispatch<'msg> -> unit)
                     (program: Program<'arg, 'model, 'msg, 'view>) =
        { program with
            setState = setState }

    /// Return the function to render the state
    let setState (program: Program<'arg, 'model, 'msg, 'view>) =
        program.setState

    /// Return the view function
    let view (program: Program<'arg, 'model, 'msg, 'view>) =
        program.view

    /// Return the init function
    let init (program: Program<'arg, 'model, 'msg, 'view>) =
        program.init

    /// Return the update function
    let update (program: Program<'arg, 'model, 'msg, 'view>) =
        program.update

    /// Map the program type
    let map mapInit mapUpdate mapView mapSetState mapSubscribe mapTermination
            (program: Program<'arg, 'model, 'msg, 'view>) =
        { init = mapInit program.init
          update = mapUpdate program.update
          view = mapView program.view
          setState = mapSetState program.setState
          subscribe = mapSubscribe program.subscribe
          onError = program.onError
          termination = mapTermination program.termination }

    module Subs = Sub.Internal

    /// Start the program loop.
    /// syncDispatch: specify how to serialize dispatch calls.
    /// arg: argument to pass to the init() function.
    /// program: program created with 'mkSimple' or 'mkProgram'.
    let runWithDispatch (syncDispatch: Dispatch<'msg> -> Dispatch<'msg>) (arg: 'arg) (program: Program<'arg, 'model, 'msg, 'view>) =
        let (model,cmd) = program.init arg
        let sub = program.subscribe model
        let toTerminate, terminate = program.termination
        let rb = RingBuffer 10
        let mutable reentered = false
        let mutable state = model
        let mutable activeSubs = Subs.empty
        let mutable terminated = false
        let rec dispatch msg =
            if not terminated then
                rb.Push msg
                if not reentered then
                    reentered <- true
                    processMsgs ()
                    reentered <- false
        and dispatch' = syncDispatch dispatch // serialized dispatch
        and processMsgs () =
            let mutable nextMsg = rb.Pop()
            while not terminated && Option.isSome nextMsg do
                let msg = nextMsg.Value
                if toTerminate msg then
                    Subs.Fx.stop program.onError activeSubs
                    terminate state
                    terminated <- true
                else
                    let (model',cmd') = program.update msg state
                    let sub' = program.subscribe model'
                    program.setState model' dispatch'
                    cmd' |> Cmd.exec (fun ex -> program.onError (sprintf "Error handling the message: %A" msg, ex)) dispatch'
                    state <- model'
                    activeSubs <- Subs.diff activeSubs sub' |> Subs.Fx.change program.onError dispatch'
                    nextMsg <- rb.Pop()

        reentered <- true
        program.setState model dispatch'
        cmd |> Cmd.exec (fun ex -> program.onError (sprintf "Error intitializing:", ex)) dispatch'
        activeSubs <- Subs.diff activeSubs sub |> Subs.Fx.change program.onError dispatch'
        processMsgs ()
        reentered <- false


    /// Start the single-threaded dispatch loop.
    /// arg: argument to pass to the 'init' function.
    /// program: program created with 'mkSimple' or 'mkProgram'.
    let runWith (arg: 'arg) (program: Program<'arg, 'model, 'msg, 'view>) = runWithDispatch id arg program

    /// Start the dispatch loop with `unit` for the init() function.
    let run (program: Program<unit, 'model, 'msg, 'view>) = runWith () program
