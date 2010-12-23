module Core.Serialization

open System.IO
open System.Runtime.Serialization.Formatters
open System.Runtime.Serialization.Formatters.Binary

let loadFromStream stream =
    let formatter = BinaryFormatter()
    formatter.Deserialize(stream)

let loadFromFile path =
    use stream = new FileStream(path, FileMode.Open)
    loadFromStream stream

let saveToStream stream obj =
    let formatter = BinaryFormatter(TypeFormat = FormatterTypeStyle.TypesWhenNeeded)
    formatter.Serialize(stream, obj)

let saveToFile path obj =
    use stream = new FileStream(path, FileMode.Create)
    saveToStream stream obj