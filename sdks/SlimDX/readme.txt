This is the custom built SlimDX assembly.

It consists of a Release build of a modified SlimDX codebase (the SlimDX.diff contains the diff).

The modifications are as follows:
- Stripped away all components except Direct3D11 and RawInput
- Stripped away resources and custom exception error messages (dxerr.lib)
- Removed ObjectTable and introduced finalizers support (no need to dispose of all objects manually)
