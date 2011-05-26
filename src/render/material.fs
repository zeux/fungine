namespace Render

type Material =
    { albedo_map: Asset<Texture> option
      normal_map: Asset<Texture> option
      specular_map: Asset<Texture> option
    }