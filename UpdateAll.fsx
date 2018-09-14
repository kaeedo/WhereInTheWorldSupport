#r "System"
#r "System.IO"
#r "System.Net.Http"

open System
open System.IO
open System.Net.Http

let (++) a b = Path.Combine(a, b)

let verboseDownloadUrl = "http://download.geonames.org/export/dump/"
let simpleDownloadUrl = "http://download.geonames.org/export/zip/"

let countryInformationFileName = "countryInformation.txt"
let supportedCountriesFileName = "supportedCountries.txt"

let countryInformationPostalCodesFileName = "countryInformationPostalCodes.txt"

let ensureCleanDirectory directory =
    if Directory.Exists(directory)
    then Directory.Delete(directory, true)

    Directory.CreateDirectory(directory) |> ignore

let downloadCountryInformation () =
    let httpClient = new HttpClient()
    let response =
        httpClient.GetStringAsync(verboseDownloadUrl + "countryInfo.txt")
        |> Async.AwaitTask
        |> Async.RunSynchronously

    if File.Exists("verbose" ++ countryInformationFileName)
    then File.Delete("verbose" ++ countryInformationFileName)

    File.WriteAllLines("verbose" ++ countryInformationFileName, response.Split('\n'))
    countryInformationFileName

let loadFile fileName = File.ReadAllLines("verbose" ++ fileName)

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

let writeSupportedCountries (supportedCountries: (string * string) seq) =
    if File.Exists("verbose" ++ supportedCountriesFileName)
    then File.Delete("verbose" ++ supportedCountriesFileName)

    let countriesList =
        supportedCountries
        |> Seq.map (fun (code, name) ->
            sprintf "%s,%s" code name
        )

    File.WriteAllLines("verbose" ++ supportedCountriesFileName, countriesList)

let writeCountryInformation (countries: (string * string * string) seq) =
    if File.Exists("simple" ++ countryInformationPostalCodesFileName)
    then File.Delete("simple" ++ countryInformationPostalCodesFileName)

    let countryPostalCodeInformation =
        countries
        |> Seq.map (fun (code, postalCodeFormat, postalCodeRegex) ->
            sprintf "%s,%s,%s" code postalCodeFormat postalCodeRegex
        )

    File.WriteAllLines("simple" ++ countryInformationPostalCodesFileName, countryPostalCodeInformation)

let readSimpleCountryList () =
    File.ReadAllLines("simple" ++ supportedCountriesFileName)
    |> Seq.filter (fun l -> l.Length > 1)
    |> Seq.map (fun l ->
        let parts = l.Split(',')
        (parts.[0], parts.[1])
    )

let batchesOf n =
    Seq.mapi (fun i v -> i / n, v) >>
    Seq.groupBy fst >>
    Seq.map snd >>
    Seq.map (Seq.map snd)

let downloadZips directory downloadUrl (supportedCountries: (string * string) seq) =
    let directoryName = directory ++ "countryFiles"
    if not (Directory.Exists(directoryName))
    then Directory.CreateDirectory(directoryName) |> ignore

    supportedCountries
    |> Seq.map (fun (code, name) ->
        async {
            let httpClient = new HttpClient()
            let! response = (httpClient.GetByteArrayAsync(sprintf "%s/%s.zip" downloadUrl code) |> Async.AwaitTask)

            let countryZipFileName = directoryName ++ (code + ".zip")

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

let downloadVerboseZipFiles =
    downloadCountryInformation
    >> loadFile
    >> deleteComments
    >> parseInformation
    >> supportedCountryList
    >> (downloadZips "verbose" verboseDownloadUrl)

let downloadSimpleZipFiles =
    readSimpleCountryList
    >> (downloadZips "simple" simpleDownloadUrl)

let createSimplePostalCodeFormatFile () =
    (downloadCountryInformation
    >> loadFile
    >> deleteComments
    >> parseInformation) ()
    |> Seq.filter (fun ci ->
        readSimpleCountryList ()
        |> Seq.exists (fun scl -> fst scl = ci.[0])
    )
    |> Seq.map (fun ci ->
        ci.[0], ci.[13], ci.[14]
    )
    |> writeCountryInformation

let createSupportedCountryList =
    downloadCountryInformation
    >> loadFile
    >> deleteComments
    >> parseInformation
    >> supportedCountryList
    >> writeSupportedCountries

let args = Environment.GetCommandLineArgs()
if args.[2] = "downloadverbose"
then
    printfn "Downloading verbose zip files"
    ensureCleanDirectory ("verbose" ++ "countryFiles")
    downloadVerboseZipFiles()

if args.[2] = "downloadsimple"
then
    printfn "Downloading simple zip files"
    ensureCleanDirectory ("simple" ++ "countryFiles")
    downloadSimpleZipFiles()

if args.[2] = "supportedcountries"
then
    printfn "Creating verbose supported country list"
    ensureCleanDirectory ("verbose" ++ "countryFiles")
    createSupportedCountryList()

if args.[2] = "postalcodeformat"
then
    printfn "Creating postal code format file for simple countries"
    createSimplePostalCodeFormatFile()

0
