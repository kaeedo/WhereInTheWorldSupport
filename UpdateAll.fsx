#r "System.IO"
open System.IO
#r "System.Net.Http"

open System.IO
open System.Net.Http

let downloadUrl = "http://download.geonames.org/export/dump/"
let countryInformationFileName = "countryInformation.txt"

let downloadCountryInformation () =
    let httpClient = new HttpClient()
    let response =
        httpClient.GetStringAsync(downloadUrl + "countryInfo.txt")
        |> Async.AwaitTask
        |> Async.RunSynchronously

    if File.Exists(countryInformationFileName)
    then File.Delete(countryInformationFileName)

    File.WriteAllLines(countryInformationFileName, response.Split('\n'))
    countryInformationFileName

let loadFile fileName =
    File.ReadAllLines(fileName)

let deleteComments (fileContents: string seq) =
    fileContents
    |> Seq.filter (fun fc ->
        fc.Length > 1 && not (fc.StartsWith("#"))
    )

let parseInformation (fileContents: string seq) =
    fileContents
    |> Seq.map (fun fc -> fc.Split('\t'))

let supportedCountryList (parsedContents: string[] seq) =
    parsedContents
    |> Seq.map (fun pc -> pc.[0], pc.[4])

let batchesOf n =
    Seq.mapi (fun i v -> i / n, v) >>
    Seq.groupBy fst >>
    Seq.map snd >>
    Seq.map (Seq.map snd)

let downloadZips (supportedCountries: (string * string) seq) =
    let directoryName = "countryFiles"
    if not (Directory.Exists(directoryName))
    then Directory.CreateDirectory(directoryName) |> ignore

    supportedCountries
    |> Seq.map (fun (code, name) ->
        async {
            let httpClient = new HttpClient()
            let! response = (httpClient.GetByteArrayAsync(sprintf "%s/%s.zip" downloadUrl code) |> Async.AwaitTask)

            let countryZipFileName = sprintf "%s\\%s.zip" directoryName code

            if File.Exists(countryZipFileName)
            then File.Delete(countryZipFileName)

            File.WriteAllBytes(countryZipFileName, response)
            printfn "Downloaded: %s" name
        }
    )
    |> batchesOf 20
    |> Seq.iter (fun batch ->
        batch
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore
    )


(downloadCountryInformation
>> loadFile
>> deleteComments
>> parseInformation
>> supportedCountryList
>> downloadZips) ()
