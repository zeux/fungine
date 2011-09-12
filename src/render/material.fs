namespace Render

type Material =
    { albedoMap: Asset<Texture> option
      normalMap: Asset<Texture> option
      specularMap: Asset<Texture> option
    }