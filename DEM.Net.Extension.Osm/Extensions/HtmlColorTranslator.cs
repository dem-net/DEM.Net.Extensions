//
// HtmlColorTranslator.cs
//
// Author:
//       Xavier Fischer 2020-3
//
// Copyright (c) 2020 Xavier Fischer
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace DEM.Net.Extension.Osm.Extensions
{
    /// <summary>
    /// Color translator that also handles html color names
    /// Source: https://wiki.openstreetmap.org/wiki/Key:colour
    /// </summary>
    public static class HtmlColorTranslator
    {

        private static Dictionary<string, Color> knownColors = new Dictionary<string, Color>();

        static HtmlColorTranslator()
        {
            #region Known colors

            knownColors.Add("aliceblue", Color.FromArgb(240, 248, 255));
            knownColors.Add("antiquewhite", Color.FromArgb(250, 235, 215));
            knownColors.Add("aqua", Color.FromArgb(0, 255, 255));
            knownColors.Add("aquamarine", Color.FromArgb(127, 255, 212));
            knownColors.Add("azure", Color.FromArgb(240, 255, 255));
            knownColors.Add("beige", Color.FromArgb(245, 245, 220));
            knownColors.Add("bisque", Color.FromArgb(255, 228, 196));
            knownColors.Add("black", Color.FromArgb(0, 0, 0));
            knownColors.Add("blanchedalmond", Color.FromArgb(255, 235, 205));
            knownColors.Add("blue", Color.FromArgb(0, 0, 255));
            knownColors.Add("blueviolet", Color.FromArgb(138, 43, 226));
            knownColors.Add("brown", Color.FromArgb(165, 42, 42));
            knownColors.Add("burlywood", Color.FromArgb(222, 184, 135));
            knownColors.Add("cadetblue", Color.FromArgb(95, 158, 160));
            knownColors.Add("chartreuse", Color.FromArgb(127, 255, 0));
            knownColors.Add("chocolate", Color.FromArgb(210, 105, 30));
            knownColors.Add("coral", Color.FromArgb(255, 127, 80));
            knownColors.Add("cornflowerblue", Color.FromArgb(100, 149, 237));
            knownColors.Add("cornsilk", Color.FromArgb(255, 248, 220));
            knownColors.Add("crimson", Color.FromArgb(220, 20, 60));
            knownColors.Add("cyan", Color.FromArgb(0, 255, 255));
            knownColors.Add("darkblue", Color.FromArgb(0, 0, 139));
            knownColors.Add("darkcyan", Color.FromArgb(0, 139, 139));
            knownColors.Add("darkgoldenrod", Color.FromArgb(184, 134, 11));
            knownColors.Add("darkgray", Color.FromArgb(169, 169, 169));
            knownColors.Add("darkgreen", Color.FromArgb(0, 100, 0));
            knownColors.Add("darkgrey", Color.FromArgb(169, 169, 169));
            knownColors.Add("darkkhaki", Color.FromArgb(189, 183, 107));
            knownColors.Add("darkmagenta", Color.FromArgb(139, 0, 139));
            knownColors.Add("darkolivegreen", Color.FromArgb(85, 107, 47));
            knownColors.Add("darkorange", Color.FromArgb(255, 140, 0));
            knownColors.Add("darkorchid", Color.FromArgb(153, 50, 204));
            knownColors.Add("darkred", Color.FromArgb(139, 0, 0));
            knownColors.Add("darksalmon", Color.FromArgb(233, 150, 122));
            knownColors.Add("darkseagreen", Color.FromArgb(143, 188, 143));
            knownColors.Add("darkslateblue", Color.FromArgb(72, 61, 139));
            knownColors.Add("darkslategray", Color.FromArgb(47, 79, 79));
            knownColors.Add("darkslategrey", Color.FromArgb(47, 79, 79));
            knownColors.Add("darkturquoise", Color.FromArgb(0, 206, 209));
            knownColors.Add("darkviolet", Color.FromArgb(148, 0, 211));
            knownColors.Add("deeppink", Color.FromArgb(255, 20, 147));
            knownColors.Add("deepskyblue", Color.FromArgb(0, 191, 255));
            knownColors.Add("dimgray", Color.FromArgb(105, 105, 105));
            knownColors.Add("dimgrey", Color.FromArgb(105, 105, 105));
            knownColors.Add("dodgerblue", Color.FromArgb(30, 144, 255));
            knownColors.Add("firebrick", Color.FromArgb(178, 34, 34));
            knownColors.Add("floralwhite", Color.FromArgb(255, 250, 240));
            knownColors.Add("forestgreen", Color.FromArgb(34, 139, 34));
            knownColors.Add("fuchsia", Color.FromArgb(255, 0, 255));
            knownColors.Add("gainsboro", Color.FromArgb(220, 220, 220));
            knownColors.Add("ghostwhite", Color.FromArgb(248, 248, 255));
            knownColors.Add("gold", Color.FromArgb(255, 215, 0));
            knownColors.Add("goldenrod", Color.FromArgb(218, 165, 32));
            knownColors.Add("gray", Color.FromArgb(128, 128, 128));
            knownColors.Add("green", Color.FromArgb(0, 128, 0));
            knownColors.Add("greenyellow", Color.FromArgb(173, 255, 47));
            knownColors.Add("grey", Color.FromArgb(128, 128, 128));
            knownColors.Add("honeydew", Color.FromArgb(240, 255, 240));
            knownColors.Add("hotpink", Color.FromArgb(255, 105, 180));
            knownColors.Add("indianred", Color.FromArgb(205, 92, 92));
            knownColors.Add("indigo", Color.FromArgb(75, 0, 130));
            knownColors.Add("ivory", Color.FromArgb(255, 255, 240));
            knownColors.Add("khaki", Color.FromArgb(240, 230, 140));
            knownColors.Add("lavender", Color.FromArgb(230, 230, 250));
            knownColors.Add("lavenderblush", Color.FromArgb(255, 240, 245));
            knownColors.Add("lawngreen", Color.FromArgb(124, 252, 0));
            knownColors.Add("lemonchiffon", Color.FromArgb(255, 250, 205));
            knownColors.Add("lightblue", Color.FromArgb(173, 216, 230));
            knownColors.Add("lightcoral", Color.FromArgb(240, 128, 128));
            knownColors.Add("lightcyan", Color.FromArgb(224, 255, 255));
            knownColors.Add("lightgoldenrodyellow", Color.FromArgb(250, 250, 210));
            knownColors.Add("lightgray", Color.FromArgb(211, 211, 211));
            knownColors.Add("lightgreen", Color.FromArgb(144, 238, 144));
            knownColors.Add("lightgrey", Color.FromArgb(211, 211, 211));
            knownColors.Add("lightpink", Color.FromArgb(255, 182, 193));
            knownColors.Add("lightsalmon", Color.FromArgb(255, 160, 122));
            knownColors.Add("lightseagreen", Color.FromArgb(32, 178, 170));
            knownColors.Add("lightskyblue", Color.FromArgb(135, 206, 250));
            knownColors.Add("lightslategray", Color.FromArgb(119, 136, 153));
            knownColors.Add("lightslategrey", Color.FromArgb(119, 136, 153));
            knownColors.Add("lightsteelblue", Color.FromArgb(176, 196, 222));
            knownColors.Add("lightyellow", Color.FromArgb(255, 255, 224));
            knownColors.Add("lime", Color.FromArgb(0, 255, 0));
            knownColors.Add("limegreen", Color.FromArgb(50, 205, 50));
            knownColors.Add("linen", Color.FromArgb(250, 240, 230));
            knownColors.Add("magenta", Color.FromArgb(255, 0, 255));
            knownColors.Add("maroon", Color.FromArgb(128, 0, 0));
            knownColors.Add("mediumaquamarine", Color.FromArgb(102, 205, 170));
            knownColors.Add("mediumblue", Color.FromArgb(0, 0, 205));
            knownColors.Add("mediumorchid", Color.FromArgb(186, 85, 211));
            knownColors.Add("mediumpurple", Color.FromArgb(147, 112, 219));
            knownColors.Add("mediumseagreen", Color.FromArgb(60, 179, 113));
            knownColors.Add("mediumslateblue", Color.FromArgb(123, 104, 238));
            knownColors.Add("mediumspringgreen", Color.FromArgb(0, 250, 154));
            knownColors.Add("mediumturquoise", Color.FromArgb(72, 209, 204));
            knownColors.Add("mediumvioletred", Color.FromArgb(199, 21, 133));
            knownColors.Add("midnightblue", Color.FromArgb(25, 25, 112));
            knownColors.Add("mintcream", Color.FromArgb(245, 255, 250));
            knownColors.Add("mistyrose", Color.FromArgb(255, 228, 225));
            knownColors.Add("moccasin", Color.FromArgb(255, 228, 181));
            knownColors.Add("navajowhite", Color.FromArgb(255, 222, 173));
            knownColors.Add("navy", Color.FromArgb(0, 0, 128));
            knownColors.Add("oldlace", Color.FromArgb(253, 245, 230));
            knownColors.Add("olive", Color.FromArgb(128, 128, 0));
            knownColors.Add("olivedrab", Color.FromArgb(107, 142, 35));
            knownColors.Add("orange", Color.FromArgb(255, 165, 0));
            knownColors.Add("orangered", Color.FromArgb(255, 69, 0));
            knownColors.Add("orchid", Color.FromArgb(218, 112, 214));
            knownColors.Add("palegoldenrod", Color.FromArgb(238, 232, 170));
            knownColors.Add("palegreen", Color.FromArgb(152, 251, 152));
            knownColors.Add("paleturquoise", Color.FromArgb(175, 238, 238));
            knownColors.Add("palevioletred", Color.FromArgb(219, 112, 147));
            knownColors.Add("papayawhip", Color.FromArgb(255, 239, 213));
            knownColors.Add("peachpuff", Color.FromArgb(255, 218, 185));
            knownColors.Add("peru", Color.FromArgb(205, 133, 63));
            knownColors.Add("pink", Color.FromArgb(255, 192, 203));
            knownColors.Add("plum", Color.FromArgb(221, 160, 221));
            knownColors.Add("powderblue", Color.FromArgb(176, 224, 230));
            knownColors.Add("purple", Color.FromArgb(128, 0, 128));
            knownColors.Add("red", Color.FromArgb(255, 0, 0));
            knownColors.Add("rosybrown", Color.FromArgb(188, 143, 143));
            knownColors.Add("royalblue", Color.FromArgb(65, 105, 225));
            knownColors.Add("saddlebrown", Color.FromArgb(139, 69, 19));
            knownColors.Add("salmon", Color.FromArgb(250, 128, 114));
            knownColors.Add("sandybrown", Color.FromArgb(244, 164, 96));
            knownColors.Add("seagreen", Color.FromArgb(46, 139, 87));
            knownColors.Add("seashell", Color.FromArgb(255, 245, 238));
            knownColors.Add("sienna", Color.FromArgb(160, 82, 45));
            knownColors.Add("silver", Color.FromArgb(192, 192, 192));
            knownColors.Add("skyblue", Color.FromArgb(135, 206, 235));
            knownColors.Add("slateblue", Color.FromArgb(106, 90, 205));
            knownColors.Add("slategray", Color.FromArgb(112, 128, 144));
            knownColors.Add("slategrey", Color.FromArgb(112, 128, 144));
            knownColors.Add("snow", Color.FromArgb(255, 250, 250));
            knownColors.Add("springgreen", Color.FromArgb(0, 255, 127));
            knownColors.Add("steelblue", Color.FromArgb(70, 130, 180));
            knownColors.Add("tan", Color.FromArgb(210, 180, 140));
            knownColors.Add("teal", Color.FromArgb(0, 128, 128));
            knownColors.Add("thistle", Color.FromArgb(216, 191, 216));
            knownColors.Add("tomato", Color.FromArgb(255, 99, 71));
            knownColors.Add("turquoise", Color.FromArgb(64, 224, 208));
            knownColors.Add("violet", Color.FromArgb(238, 130, 238));
            knownColors.Add("wheat", Color.FromArgb(245, 222, 179));
            knownColors.Add("white", Color.FromArgb(255, 255, 255));
            knownColors.Add("whitesmoke", Color.FromArgb(245, 245, 245));
            knownColors.Add("yellow", Color.FromArgb(255, 255, 0));
            knownColors.Add("yellowgreen", Color.FromArgb(154, 205, 50));
            #endregion

        }

        internal static Color FromHtml(string htmlColor)
        {
            Color color;
            if (knownColors.TryGetValue(htmlColor.ToLower(), out color))
            {
                return color;
            }
            else if (knownColors.TryGetValue(htmlColor.ToLower().Replace("_", ""), out color))
            {
                return color;
            }
            else
            {
                if (!htmlColor.StartsWith("#")) htmlColor = string.Concat("#", htmlColor);
                return ColorTranslator.FromHtml(htmlColor);
            }
        }

        public static Vector4 ToVector4(string htmlColor)
        {
            float componentRemap(byte c) => c / 255f;

            var color = Color.FromArgb(230, 230, 230);
            try
            {
                color = HtmlColorTranslator.FromHtml(htmlColor);
            }
            catch (Exception ex)
            {
            }
            return new Vector4(componentRemap(color.R), componentRemap(color.G), componentRemap(color.B), componentRemap(color.A));
        }
    }
}
