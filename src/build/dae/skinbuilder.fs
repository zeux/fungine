namespace Build.Dae

open System.Collections.Generic
open System.Xml

open Build.Dae.Parse
open Build.Geometry

// bone influences for a single vertex
type SkinVertex = BoneInfluence array

// build-time skin controller type
type Skin =
    { binding: Render.SkinBinding
      vertices: SkinVertex array }

module SkinBuilder =
    // build SkinBinding from COLLADA <skin> node
    let private buildBinding doc (conv: BasisConverter) (skin: XmlNode) (sidMap: IDictionary<string, int>) =
        // get bind shape matrix (no such concept in our data model, use it to transform invBindPose)
        let bindShapeMatrix = Build.Dae.SkeletonBuilder.parseMatrixNode (skin.SelectSingleNode "bind_shape_matrix")

        // get joint data refs
        let jointsUrl = skin.SelectSingleNode("joints/input[@semantic='JOINT']/@source").Value
        let invBindMatrixUrl = skin.SelectSingleNode("joints/input[@semantic='INV_BIND_MATRIX']/@source").Value

        // parse joint data
        let joints = getNameArray doc jointsUrl
        let invBindMatrix = getFloatArray doc invBindMatrixUrl 16
        assert (joints.Length * 16 = invBindMatrix.Length)

        // create binding
        let bones = joints |> Array.map (fun sid -> sidMap.[sid])
        let invBindPose = Array.init bones.Length (fun idx -> Build.Dae.SkeletonBuilder.parseMatrixArray invBindMatrix (idx * 16) * bindShapeMatrix) |> Array.map conv.Matrix

        Render.SkinBinding(bones, invBindPose)

    // get a set of normalized weights (with sum of 1) of bounded length, discard minimal weights
    let private normalizeWeights (weights: BoneInfluence array) maxWeights =
        // get at most maxWeights largest weights
        let sortedWeights = weights |> Array.sortBy (fun i -> -i.weight)
        let importantWeights = Array.sub sortedWeights 0 (min weights.Length maxWeights)

        // normalize weights
        let totalWeight = importantWeights |> Array.sumBy (fun i -> i.weight)
        let invTotalWeight = if totalWeight > 0.f then 1.f / totalWeight else 1.f

        importantWeights |> Array.map (fun i -> BoneInfluence(index = i.index, weight = i.weight / totalWeight))

    // get weights for all vertices from COLLADA <skin> node, normalizing and truncating weights as necessary
    let private buildVertexWeights doc (skin: XmlNode) maxWeights =
        // get vertex weights node (should be unique)
        let vertexWeights = skin.SelectSingleNode "vertex_weights"

        // get joint data (make sure that joint indices are the same as that of skin binding)
        let vertexJointInput = vertexWeights.SelectSingleNode "input[@semantic='JOINT']"
        assert (vertexJointInput.Attribute "source" = (skin.SelectSingleNode "joints/input[@semantic='JOINT']/@source").Value)
        let vertexJointOffset = int (vertexJointInput.Attribute "offset")

        // get weight data
        let vertexWeightInput = vertexWeights.SelectSingleNode "input[@semantic='WEIGHT']"
        let vertexWeightData = getFloatArray doc (vertexWeightInput.Attribute "source") 1
        let vertexWeightOffset = int (vertexWeightInput.Attribute "offset")

        // get <v> array stride (why it is not explicitly stated in the file is, again, beyond me)
        let vStride = 1 + Array.max (vertexWeights.Select "input/@offset" |> Array.map (fun attr -> int attr.Value))

        // parse <v> and <vcount> arrays
        let vcountData = getIntArray (vertexWeights.SelectSingleNode "vcount")
        assert (vcountData.Length = int (vertexWeights.Attribute "count"))

        let vData = getIntArray (vertexWeights.SelectSingleNode "v")

        // for each vertex, get an offset into <v> array (prefix sum of vcount)
        let voffset = Array.scan (fun acc count -> acc + count * vStride) 0 (Array.sub vcountData 0 (vcountData.Length - 1))

        // build vertex data
        Array.map2 (fun offset count ->
            // get basic weights, as stored in .dae
            let weights = Array.init count (fun index ->
                let influenceOffset = offset + index * vStride
                let boneIndex = vData.[influenceOffset + vertexJointOffset]
                let weightIndex = vData.[influenceOffset + vertexWeightOffset]

                BoneInfluence(index = boneIndex, weight = vertexWeightData.[weightIndex]))

            // sort, cut excessive weights and normalize
            normalizeWeights weights maxWeights
        ) voffset vcountData

    // build skin data from controller instance
    let build (doc: Document) (conv: BasisConverter) (instanceController: XmlNode) skeleton maxWeights =
        // get controller
        let controller = doc.Node (instanceController.Attribute "url")

        // get skeleton nodes (joints reference nodes via sids from skeleton subtrees)
        let skeletons = instanceController.Select "skeleton/text()" |> Array.map (fun ref -> doc.Node ref.Value)

        // get nodes with sids from skeleton subtrees
        let joints = skeletons |> Array.collect (fun node -> node.Select "descendant-or-self::node[@sid]")

        // build sid -> index map
        let sidMap = joints |> Array.map (fun node -> node.Attribute "sid", skeleton.nodeMap.[node]) |> dict

        // get controller skin (should be unique)
        let skin = controller.SelectSingleNode "skin"

        // create skin binding
        let binding = buildBinding doc conv skin sidMap

        // parse vertex binding
        let vertices = buildVertexWeights doc skin maxWeights

        { new Skin with binding = binding and vertices = vertices }