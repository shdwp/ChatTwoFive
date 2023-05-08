using System.Numerics;

namespace ChatTwoFive.Util;

internal static class ColourUtil {
    private static (byte r, byte g, byte b, byte a) RgbaToComponents(uint rgba) {
        var r = (byte) ((rgba & 0xFF000000) >> 24);
        var g = (byte) ((rgba & 0xFF0000) >> 16);
        var b = (byte) ((rgba & 0xFF00) >> 8);
        var a = (byte) (rgba & 0xFF);
        return (r, g, b, a);
    }

    internal static uint RgbaToAbgr(uint rgba) {
        var (r, g, b, a) = RgbaToComponents(rgba);
        return (uint) ((a << 24) | (b << 16) | (g << 8) | r);
    }

    internal static Vector3 RgbaToVector3(uint rgba) {
        var (r, g, b, _) = RgbaToComponents(rgba);
        return new Vector3((float) r / 255, (float) g / 255, (float) b / 255);
    }

    internal static uint Vector3ToRgba(Vector3 col) {
        return ComponentsToRgba(
            (byte) Math.Round(col.X * 255),
            (byte) Math.Round(col.Y * 255),
            (byte) Math.Round(col.Z * 255)
        );
    }

    internal static uint Vector4ToAbgr(Vector4 col) {
        return RgbaToAbgr(ComponentsToRgba(
            (byte) Math.Round(col.X * 255),
            (byte) Math.Round(col.Y * 255),
            (byte) Math.Round(col.Z * 255),
            (byte) Math.Round(col.W * 255)
        ));
    }

    internal static uint ComponentsToRgba(byte red, byte green, byte blue, byte alpha = 0xFF) => alpha
                                                                                                 | (uint) (red << 24)
                                                                                                 | (uint) (green << 16)
                                                                                                 | (uint) (blue << 8);
}
