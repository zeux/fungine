// Copyright NVIDIA Corporation 2007 -- Ignacio Castano <icastano@nvidia.com>
// 
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

#include <nvtt/nvtt.h>

#include <nvimage/Image.h>    // @@ It might be a good idea to use FreeImage directly instead of ImageIO.
#include <nvimage/ImageIO.h>
#include <nvimage/FloatImage.h>
#include <nvimage/DirectDrawSurface.h>

#include <nvcore/Ptr.h>
#include <nvcore/StrLib.h>
#include <nvcore/StdStream.h>
#include <nvcore/FileSystem.h>
#include <nvcore/Timer.h>

#include <nvtt/nvtt_wrapper.h>

typedef void (__stdcall *NvttErrorCallback)(const char* message);

__declspec(thread) NvttErrorCallback g_errorCallback;

struct MyMessageHandler: public nv::MessageHandler
{
	MyMessageHandler()
	{
		nv::debug::setMessageHandler( this );
	}

	~MyMessageHandler()
	{
		nv::debug::resetMessageHandler();
	}

	virtual void log( const char * str, va_list arg )
	{
        if (g_errorCallback)
        {
            char buf[1024];
            vsnprintf(buf, sizeof(buf), str, arg);

            g_errorCallback(buf);
        }
        else
        {
            vfprintf(stderr, str, arg);
        }
	}
} g_messageHandler;

extern "C" {

NVTT_API NvttBoolean __stdcall nvttCompressFile(const char* source, const char* target, const NvttInputOptions * inputOptionsP, const NvttCompressionOptions * compressionOptions, NvttErrorCallback errorCallback)
{
    struct ErrorCallbackScope
    {
        ErrorCallbackScope(ErrorCallback errorCallback)
        {
            g_errorCallback = errorCallback;
        }

        ~ErrorCallbackScope()
        {
            g_errorCallback = NULL;
        }
    } errorCallbackScope(errorCallback);

    nv::Path input = source;

    // Make sure input file exists.
    if (!nv::FileSystem::exists(input.str()))
    {
        nvDebug("The file '%s' does not exist.\n", input.str());
        return NVTT_False;
    }

    // Set input options.
    NvttInputOptions& inputOptions = *const_cast<NvttInputOptions*>(inputOptionsP);

    if (nv::strCaseCmp(input.extension(), ".dds") == 0)
    {
        // Load surface.
        nv::DirectDrawSurface dds(input.str());
        
        if (!dds.isValid())
        {
            nvDebug("The file '%s' is not a valid DDS file.\n", input.str());
            return NVTT_False;
        }

        if (!dds.isSupported() || dds.isTexture3D())
        {
            nvDebug("The file '%s' is not a supported DDS file.\n", input.str());
            return NVTT_False;
        }

        uint faceCount;
        if (dds.isTexture2D())
        {
            inputOptions.setTextureLayout(nvtt::TextureType_2D, dds.width(), dds.height());
            faceCount = 1;
        }
        else 
        {
            nvDebugCheck(dds.isTextureCube());
            inputOptions.setTextureLayout(nvtt::TextureType_Cube, dds.width(), dds.height());
            faceCount = 6;
        }

        uint mipmapCount = dds.mipmapCount();

        nv::Image mipmap;

        for (uint f = 0; f < faceCount; f++)
        {
            for (uint m = 0; m < mipmapCount; m++)
            {
                dds.mipmap(&mipmap, f, m); // @@ Load as float.

                inputOptions.setMipmapData(mipmap.pixels(), mipmap.width(), mipmap.height(), 1, f, m);
            }
        }
    }
    else
    {
        if (nv::strCaseCmp(input.extension(), ".exr") == 0 || nv::strCaseCmp(input.extension(), ".hdr") == 0)
        {
            nv::AutoPtr<nv::FloatImage> image(nv::ImageIO::loadFloat(input.str()));

            if (image == NULL)
            {
                nvDebug("The file '%s' is not a supported image type.\n", input.str());
                return NVTT_False;
            }

            inputOptions.setFormat(nvtt::InputFormat_RGBA_32F);
            inputOptions.setTextureLayout(nvtt::TextureType_2D, image->width(), image->height());

            for (uint i = 0; i < image->componentNum(); i++)
            {
                inputOptions.setMipmapChannelData(image->channel(i), i, image->width(), image->height());
            }
        }
        else
        {
            // Regular image.
            nv::Image image;
            if (!image.load(input.str()))
            {
                nvDebug("The file '%s' is not a supported image type.\n", input.str());
                return NVTT_False;
            }

            inputOptions.setTextureLayout(nvtt::TextureType_2D, image.width(), image.height());
            inputOptions.setMipmapData(image.pixels(), image.width(), image.height());
        }
    }

    nvtt::Context context;
    context.enableCudaAcceleration(false);

    nvtt::OutputOptions outputOptions;
    outputOptions.setFileName(target);

    return context.process(inputOptions, *compressionOptions, outputOptions) ? NVTT_True : NVTT_False;
}

}
