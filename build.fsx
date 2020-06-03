// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r "paket: groupref Build //"

#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Api
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Tools.Git
open System
open System.IO

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "src/FSharp.Control.AsyncSeq"

// File system information 
let solutionFile = "FSharp.Control.AsyncSeq.sln"

let summary = "Asynchronous sequences for F#"

let buildDir = "bin"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "fsprojects" 
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "FSharp.Control.AsyncSeq"

// The url for the raw files hosted
let gitRaw = Environment.environVarOrDefault "gitRaw" "https://raw.github.com/fsprojects"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
let release = ReleaseNotes.load "RELEASE_NOTES.md"

// --------------------------------------------------------------------------------------
// Clean build results

Target.create "Clean" (fun _ ->
    Shell.cleanDirs ["bin"; "temp"]
)

Target.create "CleanDocs" (fun _ ->
    Shell.cleanDirs ["docs/output"]
)

Target.create "Restore" (fun _ ->
    DotNet.exec id "restore" ""
    |> ignore
)

// Helper active pattern for project types
let (|Fsproj|Csproj|Vbproj|Shproj|) (projFileName:string) =
    match projFileName with
    | f when f.EndsWith("fsproj") -> Fsproj
    | f when f.EndsWith("csproj") -> Csproj
    | f when f.EndsWith("vbproj") -> Vbproj
    | f when f.EndsWith("shproj") -> Shproj
    | _                           -> failwith (sprintf "Project file %s not supported. Unknown project type." projFileName)

// Generate assembly info files with the right version & up-to-date information
Target.create "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ AssemblyInfo.Title (projectName)
          AssemblyInfo.Product project
          AssemblyInfo.Description summary
          AssemblyInfo.Version release.AssemblyVersion
          AssemblyInfo.FileVersion release.AssemblyVersion ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath: string)
        ( projectPath,
          projectName,
          System.IO.Path.GetDirectoryName(projectPath: string),
          (getAssemblyInfoAttributes projectName)
        )

    !! "src/**/*.??proj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, projectName, folderName, attributes) ->
        match projFileName with
        | Fsproj -> AssemblyInfoFile.createFSharp (folderName </> "AssemblyInfo.fs") attributes
        | Csproj -> AssemblyInfoFile.createCSharp ((folderName </> "Properties") </> "AssemblyInfo.cs") attributes
        | Vbproj -> AssemblyInfoFile.createVisualBasic ((folderName </> "My Project") </> "AssemblyInfo.vb") attributes
        | Shproj -> ()
        )
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target.create "Build" (fun _ ->
    let setMsBuildParams (defaults: MSBuild.CliArguments) =
        { defaults with
            Targets = ["Rebuild"]
            Properties = [ "Configuration","Release" ]
        }
    let setParams (defaults: DotNet.MSBuildOptions) =
        { defaults with
            MSBuildParams = setMsBuildParams defaults.MSBuildParams
        }
    DotNet.msbuild setParams solutionFile
    |> ignore)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target.create "RunTests" (fun _ ->
    try
        DotNet.test (fun p ->
            let timeout = (TimeSpan.FromMinutes 20.).Milliseconds
            { p with
                RunSettingsArguments = Some (sprintf "-- TestSessionTimeout=%i" timeout)
             }) "tests/FSharp.Control.AsyncSeq.Tests"
    finally
        Trace.publish (ImportData.Nunit NunitDataVersion.Nunit3) "FSharp.Control.AsyncSeq.TestResults.xml"
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "NuGet" (fun _ ->
    Paket.pack (fun p ->
        { p with
            OutputPath = buildDir
            Version = release.NugetVersion
            ReleaseNotes = (String.toLines release.Notes) })
)

Target.create "PublishNuget" (fun _ ->
    Paket.push(fun p ->
        { p with
            WorkingDir = buildDir })
)

// --------------------------------------------------------------------------------------
// Generate the documentation

Target.create "GenerateReferenceDocs" (fun _ ->
    let (exitcode,msgs) = Fsi.exec (fun p ->
        { p with
            NoFramework = true
            WorkingDirectory = "docs/tools" }) "generate.fsx" ["--define:RELEASE"; "--define:REFERENCE";]
    if exitcode <> 0 then
      failwith "generating reference documentation failed"
      msgs
      |> List.reduce (+)
      |> Trace.traceError
)

let generateHelp' fail debug =
    let wd = (System.Environment.CurrentDirectory + "/docs/tools")
    Trace.tracefn "Working directory %s" wd
    let args =
        if debug then ["--define:HELP";]
        else ["--define:RELEASE"; "--define:HELP";]
    let (exitcode,msgs) = Fsi.exec (fun p ->
        { p with
            NoFramework = true
            WorkingDirectory = wd
            ToolPath = Fsi.FsiTool.Internal }) (wd + "/generate.fsx") args
    if exitcode = 0 then
        Trace.traceImportant "Help generated"
    else
        if fail then
            failwith "generating help documentation failed"
        else
            Trace.traceImportant "generating help documentation failed"

let generateHelp fail =
    generateHelp' fail false

Target.create "GenerateHelp" (fun _ ->
    File.delete "docs/content/release-notes.md"
    Shell.copyFile "docs/content/" "RELEASE_NOTES.md"
    Shell.rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    File.delete "docs/content/license.md"
    Shell.copyFile "docs/content/" "LICENSE.txt"
    Shell.rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp true
)

Target.create "GenerateHelpDebug" (fun _ ->
    File.delete "docs/content/release-notes.md"
    Shell.copyFile "docs/content/" "RELEASE_NOTES.md"
    Shell.rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    File.delete "docs/content/license.md"
    Shell.copyFile "docs/content/" "LICENSE.txt"
    Shell.rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp' true true
)

Target.create "KeepRunning" (fun _ ->    
    use watcher = new FileSystemWatcher(DirectoryInfo("docs/content").FullName,"*.*")
    watcher.EnableRaisingEvents <- true
    watcher.Changed.Add(fun e -> generateHelp false)
    watcher.Created.Add(fun e -> generateHelp false)
    watcher.Renamed.Add(fun e -> generateHelp false)
    watcher.Deleted.Add(fun e -> generateHelp false)

    Trace.traceImportant "Waiting for help edits. Press any key to stop."

    System.Console.ReadKey() |> ignore

    watcher.EnableRaisingEvents <- false
    watcher.Dispose()
)

Target.create "GenerateDocs" ignore

let createIndexFsx lang =
    let content = """(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../../bin"

(**
F# Project Scaffold ({0})
=========================
*)
"""
    let targetDir = "docs/content" @@ lang
    let targetFile = targetDir @@ "index.fsx"
    Directory.ensure targetDir
    System.IO.File.WriteAllText(targetFile, System.String.Format(content, lang))

Target.create "AddLangDocs" (fun _ ->
    let args = System.Environment.GetCommandLineArgs()
    if args.Length < 4 then
        failwith "Language not specified."

    args.[3..]
    |> Seq.iter (fun lang ->
        if lang.Length <> 2 && lang.Length <> 3 then
            failwithf "Language must be 2 or 3 characters (ex. 'de', 'fr', 'ja', 'gsw', etc.): %s" lang

        let templateFileName = "template.cshtml"
        let templateDir = "docs/tools/templates"
        let langTemplateDir = templateDir @@ lang
        let langTemplateFileName = langTemplateDir @@ templateFileName

        if System.IO.File.Exists(langTemplateFileName) then
            failwithf "Documents for specified language '%s' have already been added." lang

        Directory.ensure langTemplateDir
        Shell.copy langTemplateDir [ templateDir @@ templateFileName ]

        createIndexFsx lang)
)

// --------------------------------------------------------------------------------------
// Release Scripts

Target.create "ReleaseDocs" (fun _ ->
    let tempDocsDir = "temp/gh-pages"
    Shell.cleanDir tempDocsDir
    Repository.cloneSingleBranch "" (gitHome + "/" + gitName + ".git") "gh-pages" tempDocsDir

    Shell.copyRecursive "docs/output" tempDocsDir true |> Trace.tracefn "%A"
    Staging.stageAll tempDocsDir
    Commit.exec tempDocsDir (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Branches.push tempDocsDir
)

Target.create "Release" (fun _ ->
    Staging.stageAll ""
    Commit.exec "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.push ""

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" "origin" release.NugetVersion
    
    // release on github
    GitHub.createClient (Environment.environVarOrDefault "github-user" "") (Environment.environVarOrDefault "github-pw" "")
    |> GitHub.draftNewRelease gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
    |> GitHub.publishDraft
    |> Async.RunSynchronously
)

Target.create "BuildPackage" ignore

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" ignore

"Clean"
  ==> "Restore"
  ==> "Build"
  ==> "RunTests"
  =?> ("GenerateReferenceDocs", BuildServer.isLocalBuild)
  =?> ("GenerateDocs", BuildServer.isLocalBuild)
  ==> "All"
  =?> ("ReleaseDocs", BuildServer.isLocalBuild)

"All" 
  ==> "NuGet"
  ==> "BuildPackage"

"CleanDocs"
  ==> "GenerateHelp"
  ==> "GenerateReferenceDocs"
  ==> "GenerateDocs"

"CleanDocs"
  ==> "GenerateHelpDebug"

"GenerateHelp"
  ==> "KeepRunning"
    
"ReleaseDocs"
  ==> "Release"

"BuildPackage"
  ==> "PublishNuget"
  ==> "Release"

Target.runOrDefault "All"
