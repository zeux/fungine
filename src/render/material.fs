namespace Render

type Material =
    { albedoMap: Asset.Ref<Texture> option
      normalMap: Asset.Ref<Texture> option
      specularMap: Asset.Ref<Texture> option
    }