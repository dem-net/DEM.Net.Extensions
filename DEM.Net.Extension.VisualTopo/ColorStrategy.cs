using DEM.Net.Core;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace DEM.Net.Extension.VisualTopo
{
    public interface IColorCalculator
    {
        Vector4 GetColor(VisualTopoData currentNode, Vector3 position);
    }

    internal class ColorFromModelCalculation : IColorCalculator
    {
        public Vector4 GetColor(VisualTopoData currentNode, Vector3 position) => currentNode.Set.Color;
    }
    internal class ColorFromDepthCalculation : IColorCalculator
    {
        private readonly float _maxDepth;
        private readonly ColorSpaceConverter _colorConverter;

        public ColorFromDepthCalculation(VisualTopoModel model)
        {
            _maxDepth = model.Graph.AllNodes.Min(n => n.Model.GlobalVector.Z);
            _colorConverter = new ColorSpaceConverter();
        }

        public Vector4 GetColor(VisualTopoData currentNode, Vector3 position)
        {
            float lerpAmout = _maxDepth == 0 ? 0 : Math.Abs(position.Z / _maxDepth);
            Hsv hsvColor = new Hsv(MathHelper.Lerp(0f, 360f, lerpAmout), 1, 1);
            var rgb = _colorConverter.ToRgb(hsvColor);

            return new Vector4(rgb.R, rgb.G, rgb.B, 255);
            //return Vector4.Lerp(VectorsExtensions.CreateColor(0, 255, 255), VectorsExtensions.CreateColor(0, 255, 0), lerpAmout);
        }
    }


    public static class ColorStrategy
    {
        public static IColorCalculator CreateFromModel => new ColorFromModelCalculation();
        public static IColorCalculator CreateDepthGradient(VisualTopoModel model) => new ColorFromDepthCalculation(model);


    }

}
