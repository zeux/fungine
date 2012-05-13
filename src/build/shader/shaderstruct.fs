module Build.ShaderStruct

open System
open System.IO
open System.Reflection
open System.Text

open BuildSystem

// convert name to shader name (changes naming convention from PascalCase to camelCase)
let private getShaderName (name: string) =
    name.Substring(0, 1).ToLower() + name.Substring(1)

// convert type to shader type name
let private getShaderType (typ: Type) =
    if typ.IsClass then typ.Name
    elif typ.IsEnum then "int"
    elif typ = typeof<float32> then "float"
    elif typ = typeof<int> then "int"
    elif typ = typeof<bool> then "bool"
    elif typ = typeof<Vector2> then "float2"
    elif typ = typeof<Vector3> then "float3"
    elif typ = typeof<Vector4> then "float4"
    elif typ = typeof<Matrix34> then "float3x4"
    elif typ = typeof<Matrix44> then "float4x4"
    elif typ = typeof<Color4> then "float4"
    else failwithf "Type %A is not supported in shader code" typ

// get shader array length from property
let private getShaderArrayLength (p: PropertyInfo) =
    match p.GetCustomAttribute(typeof<Render.ShaderArrayAttribute>) with
    | :? Render.ShaderArrayAttribute as attr -> sprintf "[%d]" attr.Length
    | _ -> ""

// get shader struct contents for type
let private getShaderStructContents (typ: Type) =
    let sb = StringBuilder()

    for p in typ.GetProperties(BindingFlags.Public ||| BindingFlags.Static) do
        if p.PropertyType = typeof<int> then
            sb.AppendFormat("#define {0} {1}\n", (typ.Name + "_" + p.Name).ToUpper(), p.GetValue(null)) |> ignore
    
    if sb.Length > 0 then sb.AppendLine() |> ignore

    sb.AppendFormat("struct {0}\n{1}\n", getShaderType typ, "{") |> ignore

    for (p, pt) in Render.ShaderStruct.getProperties typ do
        sb.AppendFormat("\t{0} {1}{2};\n", getShaderType pt, getShaderName p.Name, getShaderArrayLength p) |> ignore

    sb.AppendLine("};").ToString()

// get shader enum contents for type
let private getShaderEnumContents (typ: Type) =
    let sb = StringBuilder()

    for (n, v) in Array.zip $ typ.GetEnumNames() $ [| for v in typ.GetEnumValues() -> v :?> int |] do
        sb.AppendFormat("#define {0} {1}\n", (typ.Name + "_" + n).ToUpper(), v) |> ignore

    sb.ToString()

// get shader contents for type
let private getShaderContents (typ: Type) =
    if typ.IsEnum then getShaderEnumContents typ
    else getShaderStructContents typ

// header file builder
let builder =
    { new Builder("ShaderStruct") with
    override this.Build task =
        let typ = Type.GetType(task.Sources.[0].Path)

        let sb = StringBuilder()

        sb.AppendFormat("#ifndef AUTO_SHADERSTRUCT_{0}_H\n#define AUTO_SHADERSTRUCT_{0}_H\n\n", typ.Name.ToUpper()) |> ignore

        for (p, pt) in Render.ShaderStruct.getProperties typ do
            if pt.IsClass || pt.IsEnum then sb.AppendFormat("#include \"auto_{0}.h\"\n", pt.Name) |> ignore

        sb.AppendFormat("\n{0}\n#endif\n", getShaderContents typ) |> ignore
        
        File.WriteAllText(task.Targets.[0].Path, sb.ToString())
        None

    override this.Version task =
        let typ = Type.GetType(task.Sources.[0].Path)
        getShaderContents typ
    }
