open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

type IOPath = System.IO.Path
// https://github.com/TheAngryByrd/MiniScaffold
//-----------------------------------------------------------------------------
// Metadata and Configuration
//-----------------------------------------------------------------------------
let projectOrSolution = "Xfixy.sln"
let srcPath = "src" |> Path.getFullName

let winUIBinPath =
    IOPath.Combine(srcPath, "Xfixy.WinUI", "bin")
    |> Path.getFullName

let srcGlob = "*.??proj"

//-----------------------------------------------------------------------------
// Helpers
//-----------------------------------------------------------------------------

let isRelease (targets: Target list) =
    targets
    |> Seq.map (fun t -> t.Name)
    |> Seq.exists ((=) "Release")


let configuration (targets: Target list) =
    let defaultVal =
        if isRelease targets then
            "Release"
        else
            "Debug"

    match Environment.environVarOrDefault "CONFIGURATION" defaultVal with
    | "Debug" -> DotNet.BuildConfiguration.Debug
    | "Release" -> DotNet.BuildConfiguration.Release
    | config -> DotNet.BuildConfiguration.Custom config

//-----------------------------------------------------------------------------
// Target Implementations
//-----------------------------------------------------------------------------


let clean _ = [ "obj"; "dist" ] |> Shell.cleanDirs

let build ctx =
    DotNet.build (fun args -> args) projectOrSolution
    let buildConfiguration = configuration (ctx.Context.AllExecutingTargets)

    let configuration =
        match buildConfiguration with
        | DotNet.BuildConfiguration.Release -> "Release"
        | DotNet.BuildConfiguration.Debug -> "Debug"
        | DotNet.BuildConfiguration.Custom n -> n

    let sourceDir = IOPath.Combine(srcPath, "Xfixy", "bin", configuration, "net7.0")
    let destinationDir = IOPath.Combine(winUIBinPath, "Xfixy")
    Trace.traceImportantfn $"SourceDir {sourceDir}. DestinationDir {destinationDir}."
    Shell.copyDir destinationDir sourceDir (fun it -> true)

let initTargets () =
    /// Defines a dependency - y is dependent on x
    let (==>!) x y = x ==> y |> ignore
    /// Defines a soft dependency. x must run before y, if it is present, but y does not require x to be run.
    let (?=>!) x y = x ?=> y |> ignore
    //-----------------------------------------------------------------------------
    // Target Declaration
    //-----------------------------------------------------------------------------
    Target.create "Clean" ignore
    Target.create "Build" build
    //-----------------------------------------------------------------------------
    // Target Dependencies
    //-----------------------------------------------------------------------------

    "Clean" ==>! "Build"

//-----------------------------------------------------------------------------
// Target Start
//-----------------------------------------------------------------------------
[<EntryPoint>]
let main argv =
    argv
    |> Array.toList
    |> Context.FakeExecutionContext.Create false "build.fsx"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

    initTargets ()
    Target.runOrDefaultWithArguments "Build"

    0 // return an integer exit code
