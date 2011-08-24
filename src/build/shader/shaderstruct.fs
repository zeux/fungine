module Build.ShaderStruct

open System
open System.IO
open System.Reflection
open System.Text
open System.Text.RegularExpressions

open BuildSystem

// convert name to shader name (changes naming convention)
let private getShaderName (name: string) =
    Regex.Split(name, "(?<=[^A-Z])(?=[A-Z])") |> Array.map (fun s -> s.ToLower()) |> String.concat "_"

// convert type to shader type name
let private getShaderType (typ: Type) =
    if typ.IsClass then typ.Name
    else if typ = typedefof<float32> then "float"
    else if typ = typedefof<int> then "int"
    else if typ = typedefof<bool> then "bool"
    else if typ = typedefof<Vector2> then "float2"
    else if typ = typedefof<Vector3> then "float3"
    else if typ = typedefof<Vector4> then "float4"
    else if typ = typedefof<Matrix34> then "float3x4"
    else if typ = typedefof<Matrix44> then "float4x4"
    else failwithf "Type %A is not supported in shader code" typ

// get shader struct contents for type
let private getShaderStructContents (typ: Type) =
    let sb = StringBuilder()

    sb.AppendFormat("struct {0}\n{1}\n", getShaderType typ, "{") |> ignore

    for p in Render.ShaderStruct.getProperties typ do
        sb.AppendFormat("\t{0} {1};\n", getShaderType p.PropertyType, getShaderName p.Name) |> ignore

    sb.AppendLine("};").ToString()

// header file builder
let builder =
    { new Builder("ShaderStruct") with
    override this.Build task =
        let typ = Type.GetType(task.Sources.[0].Path)

        let sb = StringBuilder()

        sb.AppendFormat("#ifndef AUTO_SHADERSTRUCT_{0}_H\n#define AUTO_SHADERSTRUCT_{0}_H\n\n", typ.Name.ToUpper()) |> ignore

        for p in Render.ShaderStruct.getProperties typ do
            let pt = p.PropertyType
            if pt.IsClass then sb.AppendFormat("#include \"auto_{0}.h\"\n", pt.Name) |> ignore

        sb.AppendFormat("\n{0}\n#endif\n", getShaderStructContents typ) |> ignore
        
        File.WriteAllText(task.Targets.[0].Path, sb.ToString())
        None

    override this.Version task =
        let typ = Type.GetType(task.Sources.[0].Path)
        getShaderStructContents typ
    }