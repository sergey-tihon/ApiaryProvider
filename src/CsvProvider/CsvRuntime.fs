﻿// --------------------------------------------------------------------------------------
// CSV type provider runtime components
// --------------------------------------------------------------------------------------

namespace FSharp.Data.RuntimeImplementation

open System
open System.Diagnostics
open System.IO
open System.Text

module CsvReader = 
  /// Lazily reads the specified CSV file using the specified separator
  /// (Handles most of the RFC 4180 - most notably quoted values and also
  /// quoted newline characters in columns)
  let readCsvFile (reader:TextReader) sep =
    let inline (|Char|) (n:int) = char n
    let inline (|Separator|_|) (n:int) = if Array.exists ((=) (char n)) sep then Some() else None

    /// Read quoted string value until the end (ends with end of stream or
    /// the " character, which can be encoded using double ")
    let rec readString chars = 
      match reader.Read() with
      | -1 -> chars
      | Char '"' when reader.Peek() = int '"' ->
          reader.Read() |> ignore
          readString ('"'::chars)
      | Char '"' -> chars
      | Char c -> readString (c::chars)
  
    /// Reads a line with data that are separated using specified Separators
    /// and may be quoted. Ends with newline or end of input.
    let rec readLine data chars = 
      match reader.Read() with
      | -1 | Char '\r' | Char '\n' -> 
          let item = new String(chars |> List.rev |> Array.ofList)
          item::data
      | Separator -> 
          let item = new String(chars |> List.rev |> Array.ofList)
          readLine (item::data) [] 
      | Char '"' ->
          readLine data (readString chars)
      | Char c ->
          readLine data (c::chars)

    /// Reads multiple lines from the input, skipping newline characters
    let rec readLines () = seq {
      match reader.Peek() with
      | -1 -> ()
      | Char '\r' | Char '\n' -> reader.Read() |> ignore; yield! readLines()
      | _ -> 
          yield readLine [] [] |> List.rev |> Array.ofList
          yield! readLines() }
  
    readLines() 

/// Simple type that represents a single CSV row
[<DebuggerDisplay("{Display}")>]
[<StructuredFormatDisplay("{Display}")>]
type CsvRow internal (data:string[], headers:string[]) =

  member x.Columns = data

  member private x.Display =
    let sb = new StringBuilder()
    let append (s:string) = sb.Append s |> ignore
    append "{" |> ignore
    for (header, data) in Seq.zip headers data do
        append " "
        append header
        append " = "
        append data
        append " ;"
    sb.ToString(0, sb.Length - 1) + "}";

// Simple type wrapping CSV data
type CsvFile (reader:TextReader, ?sep:string) =

  let sep = defaultArg sep ""
  let sep = if String.IsNullOrEmpty(sep) then "," else sep

  /// Read the input and cache it (we can read input only once)
  let file = CsvReader.readCsvFile reader (sep.ToCharArray()) |> Seq.cache
  let headers = file |> Seq.head
  let data = file |> Seq.skip 1 |> Seq.map (fun v -> CsvRow(v, headers))  

  member x.Data = data
  member x.Headers = headers

  interface IDisposable with
    member __.Dispose() = reader.Dispose()