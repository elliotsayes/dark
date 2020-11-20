module Tests.BwdServer

open Expecto

open LibExecution
open BwdServer

open System.Threading.Tasks
open System.IO
open System.Threading
open System.Net
open System.Net.Sockets
open System.Text.RegularExpressions


let t name =
  testTask $"Httpfiles: {name}" {
    // TODO: This test relies on the server running already. Run the server
    // instead as part of the test suite.
    let toBytes (str : string) = System.Text.Encoding.ASCII.GetBytes str
    let toStr (bytes : byte array) = System.Text.Encoding.ASCII.GetString bytes

    let setHeadersToCRLF (text : byte array) : byte array =
      // We keep our test files with an LF line ending, but the HTTP spec
      // requires headers (but not the body) to have CRLF line endings
      let mutable justSawNewline = false
      let mutable inHeaderSection = true
      text
      |> Array.toList
      |> List.collect (fun b ->
           justSawNewline <- false
           if inHeaderSection && b = byte '\n' then
             if justSawNewline then inHeaderSection <- false
             justSawNewline <- true
             [ byte '\r'; b ]
           else
             [ b ])
      |> List.toArray

    let request, expectedResponse, progString, httpMethod, httpPath =
      let filename = $"tests/httptestfiles/{name}"
      let contents = filename |> System.IO.File.ReadAllBytes |> toStr

      // TODO: use FsRegex instead
      let options = System.Text.RegularExpressions.RegexOptions.Singleline

      let m =
        Regex.Match
          (contents,
           "^\[http-handler (\S+) (\S+)\]\n(.*)\n\[request\]\n(.*)\[response\]\n(.*)$",
           options)

      if not m.Success then failwith $"incorrect format in {name}"
      let g = m.Groups
      g.[4].Value, g.[5].Value, g.[3].Value, g.[1].Value, g.[2].Value

    let (source : Runtime.Expr) =
      progString |> FSharpToExpr.parse |> FSharpToExpr.convertToExpr

    let (handler : Framework.Handler.T) =
      let id = Runtime.id

      let ids : Framework.Handler.ids =
        { moduleID = id 1; nameID = id 2; modifierID = id 3 }

      { tlid = id 7
        ast = source
        spec =
          Framework.Handler.HTTP(path = httpPath, method = httpMethod, ids = ids) }

    let! ownerID = LibBackend.Serialization.userIDForUsername "test"
    let! canvasID = LibBackend.Serialization.canvasIDForCanvas ownerID $"test-{name}"

    do! LibBackend.Serialization.saveHttpHandlersToCache
          canvasID
          ownerID
          [ Framework.TLHandler handler ]

    // Web server might not be loaded yet
    let client = new TcpClient()

    let mutable connected = false

    for i in 1 .. 10 do
      try
        if not connected then
          do! client.ConnectAsync("127.0.0.1", 9001)
          connected <- true
      with _ -> do! System.Threading.Tasks.Task.Delay 1000

    let stream = client.GetStream()
    stream.ReadTimeout <- 1000 // responses should be instant, right?
    stream.Write(request, 0, request.Length)

    let length = 10000
    let response = Array.zeroCreate length
    let byteCount = stream.Read(response, 0, length)
    let response = Array.take byteCount response

    stream.Close()
    client.Close()

    let response =
      FsRegEx.replace
        "Date: ..., .. ... .... ..:..:.. ..."
        "Date: XXX, XX XXX XXXX XX:XX:XX XXX"
        (toStr response)

    Expect.equal expectedResponse response "Result should be ok"
  }

let testsFromFiles =
  // get all files
  let dir = "tests/httptestfiles/"
  System.IO.Directory.GetFiles(dir, "*")
  |> Array.map (System.IO.Path.GetFileName)
  |> Array.toList
  |> List.map t

let testMany (name : string) (fn : 'a -> 'b) (values : List<'a * 'b>) =
  testList
    name
    (List.mapi (fun i (input, expected) ->
      test $"{name} - {i}" { Expect.equal (fn input) expected "" }) values)

let testManyTask (name : string) (fn : 'a -> Task<'b>) (values : List<'a * 'b>) =
  testList
    name
    (List.mapi (fun i (input, expected) ->
      testTask $"{name} - {i}" {
        let! result = fn input
        Expect.equal result expected ""
      }) values)



let unitTests =
  [ testMany
      "sanitizeUrlPath"
      BwdServer.sanitizeUrlPath
      [ ("//", "/")
        ("/abc//", "/abc")
        ("/", "/")
        ("/abcabc//xyz///", "/abcabc/xyz")
        ("", "/") ]
    testMany
      "ownerNameFromHost"
      LibBackend.Serialization.ownerNameFromHost
      [ ("test-something", "test"); ("test", "test"); ("test-many-hyphens", "test") ]
    testManyTask
      "canvasNameFromHost"
      BwdServer.canvasNameFromHost
      [ ("test-something.builtwithdark.com", "test-something")
        ("my-canvas.builtwithdark.localhost", "my-canvas")
        ("builtwithdark.localhost", "builtwithdark")
        ("my-canvas.darkcustomdomain.com", "my-canvas") ] ]

let tests =
  testList
    "BwdServer"
    [ testList "From files" testsFromFiles; testList "unit tests" unitTests ]
