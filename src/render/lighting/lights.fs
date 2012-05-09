namespace Render.Lighting

type DirectionalLight =
    { direction: Vector3
      color: Color4
      intensity: float32 }

type PointLight =
    { position: Vector3
      radius: float32
      color: Color4
      intensity: float32 }

type SpotLight =
    { position: Vector3
      direction: Vector3
      radius: float32
      outerAngle: float32
      innerAngle: float32
      color: Color4
      intensity: float32 }

type Light =
    | DirectionalLight of DirectionalLight
    | PointLight of PointLight
    | SpotLight of SpotLight