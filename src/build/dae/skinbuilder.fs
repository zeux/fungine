namespace Build.Dae

open System.Xml
open System.Collections.Generic
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
    let private buildBinding doc (conv: BasisConverter) (skin: XmlNode) (sid_map: IDictionary<string, int>) =
        // get bind shape matrix (no such concept in our data model, use it to transform inv_bind_pose)
        let bind_shape_matrix = Build.Dae.SkeletonBuilder.parseMatrixNode (skin.SelectSingleNode "bind_shape_matrix")

        // get joint data refs
        let joints_url = skin.SelectSingleNode("joints/input[@semantic='JOINT']/@source").Value
        let inv_bind_matrix_url = skin.SelectSingleNode("joints/input[@semantic='INV_BIND_MATRIX']/@source").Value

        // parse joint data
        let joints = getNameArray doc joints_url
        let inv_bind_matrix = getFloatArray doc inv_bind_matrix_url 16
        assert (joints.Length * 16 = inv_bind_matrix.Length)

        // create binding
        let bones = joints |> Array.map (fun sid -> sid_map.[sid])
        let inv_bind_pose = Array.init bones.Length (fun idx -> Build.Dae.SkeletonBuilder.parseMatrixArray inv_bind_matrix (idx * 16) * bind_shape_matrix) |> Array.map conv.Matrix

        Render.SkinBinding(bones, inv_bind_pose)

    // get a set of normalized weights (with sum of 1) of bounded length, discard minimal weights
    let private normalizeWeights (weights: BoneInfluence array) max_weights =
        // get at most max_weights largest weights
        let sorted_weights = weights |> Array.sortBy (fun i -> -i.weight)
        let important_weights = Array.sub sorted_weights 0 (min weights.Length max_weights)

        // normalize weights
        let total_weight = important_weights |> Array.sumBy (fun i -> i.weight)
        let inv_total_weight = if total_weight > 0.f then 1.f / total_weight else 1.f

        important_weights |> Array.map (fun i -> BoneInfluence(index = i.index, weight = i.weight / total_weight))

    // get weights for all vertices from COLLADA <skin> node, normalizing and truncating weights as necessary
    let private buildVertexWeights doc (skin: XmlNode) max_weights =
        // get vertex weights node (should be unique)
        let vertex_weights = skin.SelectSingleNode "vertex_weights"

        // get joint data (make sure that joint indices are the same as that of skin binding)
        let vertex_joint_input = vertex_weights.SelectSingleNode "input[@semantic='JOINT']"
        assert (vertex_joint_input.Attribute "source" = (skin.SelectSingleNode "joints/input[@semantic='JOINT']/@source").Value)
        let vertex_joint_offset = int (vertex_joint_input.Attribute "offset")

        // get weight data
        let vertex_weight_input = vertex_weights.SelectSingleNode "input[@semantic='WEIGHT']"
        let vertex_weight_data = getFloatArray doc (vertex_weight_input.Attribute "source") 1
        let vertex_weight_offset = int (vertex_weight_input.Attribute "offset")

        // get <v> array stride (why it is not explicitly stated in the file is, again, beyond me)
        let v_stride = 1 + Array.max (vertex_weights.Select "input/@offset" |> Array.map (fun attr -> int attr.Value))

        // parse <v> and <vcount> arrays
        let vcount_data = getIntArray (vertex_weights.SelectSingleNode "vcount")
        assert (vcount_data.Length = int (vertex_weights.Attribute "count"))

        let v_data = getIntArray (vertex_weights.SelectSingleNode "v")

        // for each vertex, get an offset into <v> array (prefix sum of vcount)
        let voffset = Array.scan (fun acc count -> acc + count * v_stride) 0 (Array.sub vcount_data 0 (vcount_data.Length - 1))

        // build vertex data
        Array.map2 (fun offset count ->
            // get basic weights, as stored in .dae
            let weights = Array.init count (fun index ->
                let influence_offset = offset + index * v_stride
                let bone_index = v_data.[influence_offset + vertex_joint_offset]
                let weight_index = v_data.[influence_offset + vertex_weight_offset]

                BoneInfluence(index = bone_index, weight = vertex_weight_data.[weight_index]))

            // sort, cut excessive weights and normalize
            normalizeWeights weights max_weights
        ) voffset vcount_data

    // build skin data from controller instance
    let build (doc: Document) (conv: BasisConverter) (instance_controller: XmlNode) skeleton max_weights =
        // get controller
        let controller = doc.Node (instance_controller.Attribute "url")

        // get skeleton nodes (joints reference nodes via sids from skeleton subtrees)
        let skeletons = instance_controller.Select "skeleton/text()" |> Array.map (fun ref -> doc.Node ref.Value)

        // get nodes with sids from skeleton subtrees
        let joints = skeletons |> Array.collect (fun node -> node.Select "descendant-or-self::node[@sid]")

        // build sid -> index map
        let sid_map = joints |> Array.map (fun node -> node.Attribute "sid", skeleton.node_map.[node]) |> dict

        // get controller skin (should be unique)
        let skin = controller.SelectSingleNode "skin"

        // create skin binding
        let binding = buildBinding doc conv skin sid_map

        // parse vertex binding
        let vertices = buildVertexWeights doc skin max_weights

        { new Skin with binding = binding and vertices = vertices }