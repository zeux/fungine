/*
 * BMP file loader
 *
 * Original code provided by Michael Sweet, 
 * Adapted for nVidia Texture Tools by Aranda Morrison
 */

#ifndef NV_IMAGE_BMPFILE_H
#define NV_IMAGE_BMPFILE_H

#include <nvcore/Stream.h>

namespace nv
{

typedef struct _BmpHeader    /**** BMP file header structure ****/
{
    uint16 bfType;           /* Magic number for file */
    uint32 bfSize;           /* Size of file */
    uint16 bfReserved1;      /* Reserved */
    uint16 bfReserved2;      /* ... */
    uint32 bfOffBits;        /* Offset to bitmap data */
} BmpHeader;

#define BF_TYPE 0x4D42             /* "MB" */

typedef struct _BmpInfoHeader  /**** BMP file info structure ****/
{
    uint32  biSize;           /* Size of info header */
    int32   biWidth;          /* Width of image */
    int32   biHeight;         /* Height of image */
    uint16  biPlanes;         /* Number of color planes */
    uint16  biBitCount;       /* Number of bits per pixel */
    uint32  biCompression;    /* Type of compression to use */
    uint32  biSizeImage;      /* Size of image data */
    int32   biXPelsPerMeter;  /* X pixels per meter */
    int32   biYPelsPerMeter;  /* Y pixels per meter */
    uint32  biClrUsed;        /* Number of colors used */
    uint32  biClrImportant;   /* Number of important colors */
} BmpInfoHeader;

typedef struct _BmpColor             /**** Colormap entry structure ****/
{
    uint8 b;    /* Blue value */
    uint8 g;    /* Green value */
    uint8 r;    /* Red value */
    uint8 a;    /* Reserved */
} BmpColor;

typedef struct _BmpInfo /**** Bitmap information structure ****/
{
    BmpInfoHeader bmiHeader;      /* Image header */
    BmpColor      bmiColors[256]; /* Image colormap */
} BmpInfo;


/*
* Stream operator for BMP header
*/

inline Stream & operator<< (Stream & s, BmpHeader & head)
{
    s << head.bfType;           /* Magic number for file */
    s << head.bfSize;           /* Size of file */
    s << head.bfReserved1;      /* Reserved */
    s << head.bfReserved2;      /* ... */
    s << head.bfOffBits;
    return s;
};

} /* namespace nv */

#endif /* !NV_IMAGE_BMPFILE_H */
