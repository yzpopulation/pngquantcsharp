using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            var b = Process("C:\\Users\\admin\\Desktop\\GreenScreenBG02.png", "C:\\Users\\admin\\Desktop\\GreenScreenBG02-20-50.png", 80, 90, 1);

            Console.WriteLine(Imagequant.liq_version());
        }
        public static bool Process(string inFileName, string outFilename, int minQuality, int maxQuality, int speed)
        {

            try
            {

                if (File.Exists(outFilename))
                {
                    File.Delete(outFilename);
                }

                byte[] bmpIn = File.ReadAllBytes(inFileName);
                byte[] bmpOUt = Process(bmpIn, minQuality, maxQuality, speed);
                if (null == bmpOUt)
                {
                    Console.WriteLine($"Error creating new PNG");
                    return false;
                }
                File.WriteAllBytes(outFilename, bmpOUt);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"unexpected error: {ex}");
                return false;
            }
        }
        public static byte[] Process(byte[] bytesPngIn, int minQuality, int maxQuality, int speed)
        {

            try
            {
                using MemoryStream ms = new MemoryStream(bytesPngIn);
                using Bitmap bmp = (Bitmap)Image.FromStream(ms);
                BitmapData bmpData = bmp.LockBits(
                    new Rectangle(0, 0, bmp.Width, bmp.Height)
                    , ImageLockMode.ReadOnly
                    , PixelFormat.Format32bppArgb
                );
                long bmpDataLength = bmpData.Stride * bmpData.Height;
                var origRaw = new byte[bmpDataLength];
                Marshal.Copy(bmpData.Scan0, origRaw, 0, origRaw.Length);
                bmp.UnlockBits(bmpData);


                //Stride is in BGRA
                // we need RGBA!!!!!!!!
                for (int i = 2; i < origRaw.Length; i += 4)
                {
                    byte tmp = origRaw[i];
                    origRaw[i] = origRaw[i - 2];
                    origRaw[i - 2] = tmp;
                }


                byte[] bmpOut = Compress(origRaw, bmp.Width, bmp.Height, minQuality: minQuality, maxQuality: maxQuality, speed: speed);
                if (null != bmpOut) return bmpOut;
                Console.WriteLine($"Error creating new PNG");
                return null;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"unexpected error: {ex}");
                return null;
            }
        }
		public static byte[] Compress(
			byte[] bytesOrig
			, int width
			, int height
			, double gamma = 0.0d
			, int minQuality = 50
			, int maxQuality = 90
			, int speed = 1
		)
		{

			IntPtr ptrAttr = IntPtr.Zero;
			IntPtr ptrImgSrc = IntPtr.Zero;
			IntPtr ptrImgResult = IntPtr.Zero;

			try
			{

				ptrAttr = Imagequant.liq_attr_create();
				if (ptrAttr == IntPtr.Zero)
				{
					Console.WriteLine("can't create attr");
					return null;
				}

				ptrImgSrc = Imagequant.liq_image_create_rgba(ptrAttr, bytesOrig, width, height, gamma);
				if (ptrImgSrc == IntPtr.Zero)
				{
					Console.WriteLine("can't create image");
					return null;
				}

				var errQual = Imagequant.liq_set_quality(ptrAttr, minQuality, maxQuality);
				if (LiqError.LIQ_OK != errQual)
				{
					Console.WriteLine("can't set quality");
					return null;
				}
				var errSpeed = Imagequant.liq_set_speed(ptrAttr, speed);
				if (LiqError.LIQ_OK != errSpeed)
				{
					Console.WriteLine("can't set speed");
					return null;
				}

				ptrImgResult = Imagequant.liq_quantize_image(ptrAttr, ptrImgSrc);
				if (ptrImgResult == IntPtr.Zero)
				{
					Console.WriteLine("can't quantize image");
					return null;
				}

				//var buffer_size = width * height;
				// !!!!! 4x for ARGB
				//var buffer_size = width * height * 4; 
				var bufferSize = bytesOrig.Length;
				var bytesRemapped = new byte[bufferSize];

				var err = Imagequant.liq_write_remapped_image(ptrImgResult, ptrImgSrc, bytesRemapped, (UIntPtr)bufferSize);
				if (err != LiqError.LIQ_OK)
				{
					Console.WriteLine("remapping error");
					return null;
				}


				// APPLY PALETTE
				var palStructure = Marshal.PtrToStructure(Imagequant.liq_get_palette(ptrImgResult), typeof(LiqPalette));
                if (palStructure is null)
                {
                    Console.WriteLine("get palette error");
                    return null;
                }
				LiqPalette liqPal = (LiqPalette) palStructure;

                using Bitmap bmpOut = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
                ColorPalette pal = bmpOut.Palette;

                //make sure that we only use as many entries as we have available
                int liqPalCnt = liqPal.count;
                int bmpPalCnt = pal.Entries.Length;
                int palCnt = liqPalCnt < bmpPalCnt ? liqPalCnt : bmpPalCnt;

                for (int i = 0; i < palCnt; i++)
                {
                    LiqColor liqCol = liqPal.entries[i];
                    pal.Entries[i] = Color.FromArgb(liqCol.a, liqCol.r, liqCol.g, liqCol.b);
                }

                // if there are more bmp entries than liq entries, set to invisible
                if (liqPalCnt < bmpPalCnt)
                {
                    for (int i = liqPalCnt; i < bmpPalCnt; i++)
                    {
                        pal.Entries[i] = Color.FromArgb(0, 0, 0, 0);
                    }
                }

                //!!!!!
                // Palette IS NOT A REFERENCE!!! HAS TO BE SET AGAIN!!!!!
                bmpOut.Palette = pal;

                BitmapData bmpData = bmpOut.LockBits(
                    new Rectangle(0, 0, bmpOut.Width, bmpOut.Height)
                    , ImageLockMode.WriteOnly
                    //, bmpOut.PixelFormat
                    //, PixelFormat.Format32bppArgb
                    //, PixelFormat.Format32bppRgb
                    //, PixelFormat.Format32bppPArgb
                    , PixelFormat.Format8bppIndexed
                );
                int bmpDataLength = bmpData.Stride * bmpData.Height;
                Marshal.Copy(bytesRemapped, 0, bmpData.Scan0, bmpDataLength /* compressed.Length*/);
                // !!!!! JUST FOR TESTING, write orignal data
                //Marshal.Copy(orig, 0, bmpData.Scan0, bmpDataLength /* compressed.Length*/);
                bmpOut.UnlockBits(bmpData);


                using MemoryStream msOut = new MemoryStream();
                bmpOut.Save(msOut, ImageFormat.Png);

                return msOut.GetBuffer();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"unexpected error {ex}");
				return null;
			}
			finally
			{
				Imagequant.liq_image_destroy(ptrImgSrc);
				Imagequant.liq_result_destroy(ptrImgResult);
				Imagequant.liq_attr_destroy(ptrAttr);

			}
		}


	}
}
