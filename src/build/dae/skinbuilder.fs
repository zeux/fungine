﻿namespace Build.Dae

open System.Xml
open System.Collections.Generic
open Build.Dae.Parse

type SkinVertex = Build.Geometry.BoneInfluence array

type Skin =
    { binding: Render.SkinBinding
      vertices: SkinVertex array }

module SkinBuilder =
    let private buildBinding doc (skin: XmlNode) (skeleton: Build.Dae.Skeleton) =
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
        let bones = joints |> Array.map (fun sid -> skeleton.sid_map.[sid])
        let inv_bind_pose = [|0..bones.Length-1|] |> Array.map (fun idx -> bind_shape_matrix * Build.Dae.SkeletonBuilder.parseMatrixArray inv_bind_matrix (idx * 16))

        Render.SkinBinding(bones, inv_bind_pose)

    let private buildVertexWeights doc (skin: XmlNode) =
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
            [|0..count-1|] |> Array.map (fun index ->
                let influence_offset = offset + index * v_stride
                let bone_index = v_data.[influence_offset + vertex_joint_offset]
                let weight_index = v_data.[influence_offset + vertex_weight_offset]
                Build.Geometry.BoneInfluence(index = bone_index, weight = vertex_weight_data.[weight_index]))
        ) voffset vcount_data

    let build doc (controller: XmlNode) skeleton =
        // get controller skin (should be unique)
        let skin = controller.SelectSingleNode "skin"

        // create skin binding
        let binding = buildBinding doc skin skeleton

        // parse vertex binding
        let vertices = buildVertexWeights doc skin

        { new Skin with binding = binding and vertices = vertices }