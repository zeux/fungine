This is a custom DLL build of NVidia Texture Tools; it exposes a file compression interface (in addition to nvtt_wrapper.h-based one).
The build does not use FreeImage (for DLL size reasons), and instead uses the internal loading code, with the BMP loading patch by
roondini (see http://code.google.com/p/nvidia-texture-tools/issues/detail?id=128)

In order to build this, you have to get the following:

NVidia Texture Tools:
svn co http://nvidia-texture-tools.googlecode.com/svn/trunk nvtt

libjpeg:
http://sourceforge.net/projects/libjpeg/files/libjpeg/6b/jpegsr6.zip

libpng:
http://sourceforge.net/projects/libpng/files/libpng14/1.4.4/libpng-1.4.4.tar.gz

zlib:
http://zlib.net/zlib-1.2.5.tar.gz

After that just build the solution, and copy the Release\nvtt.dll file to nvtt.dll.
