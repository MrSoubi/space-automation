using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpaceAutomation.Server;

// A small native SDL surface. No UI toolkit or client polling is needed: the
// session publishes its pause flag safely across threads. All SDL calls stay
// on the entry thread, including creation and cleanup.
internal sealed class ParticleWindow : IDisposable
{
    private const int Width = 760, Height = 520;
    private readonly Sdl.HitTest _hitTest = (_, _, _) => 1; // draggable everywhere
    private IntPtr _window, _renderer;
    private bool _initialized;

    public ParticleWindow()
    {
        try
        {
            if (Sdl.SDL_Init(0x20) != 0) // SDL_INIT_VIDEO
                throw Error("Cannot initialize the particle window");
            _initialized = true;
            _window = Sdl.SDL_CreateWindow("Space Automation", 0x2fff0000, 0x2fff0000,
                Width, Height, 0x10 | 0x2000); // BORDERLESS | ALLOW_HIGHDPI
            if (_window == IntPtr.Zero) throw Error("Cannot create the particle window");

            _renderer = Sdl.SDL_CreateRenderer(_window, -1, 0x2 | 0x4); // accelerated, vsync
            if (_renderer == IntPtr.Zero)
                _renderer = Sdl.SDL_CreateRenderer(_window, -1, 0x1); // software fallback
            if (_renderer == IntPtr.Zero) throw Error("Cannot create the particle renderer");
            Sdl.SDL_RenderSetLogicalSize(_renderer, Width, Height);
            Sdl.SDL_SetRenderDrawBlendMode(_renderer, 1);

            // Renderer creation may recreate the native window for OpenGL.
            // Compositors that do not support window opacity keep an opaque grey
            // background. Animation and the server continue to work normally.
            Sdl.SDL_SetWindowOpacity(_window, 0.82f);
            Sdl.SDL_SetWindowHitTest(_window, _hitTest, IntPtr.Zero);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Run(GameSession session, CancellationToken stopping)
    {
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed.TotalSeconds;
        double phase = 0;
        while (!stopping.IsCancellationRequested)
        {
            var frameStart = clock.Elapsed.TotalSeconds;
            while (Sdl.SDL_PollEvent(out var input) != 0)
            {
                if (input.Type == 0x100 || // SDL_QUIT (including Alt+F4)
                    (input.Type == 0x200 && input.WindowEvent == 14) ||
                    (input.Type == 0x300 && input.KeyCode == 27)) return;
                if (input.Type == 0x300 && input.Repeat == 0 && input.KeyCode == 32)
                    session.PauseOrResume();
            }

            bool paused = session.Paused;
            // Integrate elapsed time so a state change never jumps the field.
            phase += Math.Min(frameStart - previous, 0.1) * (paused ? 0.045 : 1.0);
            previous = frameStart;
            Draw(phase, paused);

            // Also cap software rendering / displays without working vsync.
            var remaining = 1.0 / 60 - (clock.Elapsed.TotalSeconds - frameStart);
            if (remaining > 0) Thread.Sleep(TimeSpan.FromSeconds(remaining));
        }
    }

    private void Draw(double time, bool paused)
    {
        Sdl.SDL_SetRenderDrawColor(_renderer, 48, 50, 54, 255);
        Sdl.SDL_RenderClear(_renderer);
        byte red = paused ? (byte)244 : (byte)82;
        byte green = paused ? (byte)91 : (byte)157;
        byte blue = paused ? (byte)104 : (byte)248;

        // Layered sheets of tiny points ripple and drift like a soft fabric.
        // Overscan the lattice so the waves flow beyond the edges of the window.
        for (int layer = 0; layer < 2; layer++)
        for (int row = -8; row < 66; row++)
        for (int column = -8; column < 94; column++)
        {
            double u = column * 9.5, v = row * 9.5;
            double wave = Math.Sin(u * 0.010 + v * 0.005 + time * 0.28 + layer * 2.4);
            double fold = Math.Cos(v * 0.014 - u * 0.003 - time * 0.21);
            float x = (float)(u + 25 * wave + 13 * Math.Sin(v * 0.013 + time * 0.17));
            float y = (float)(v + 31 * fold + 17 * wave + layer * 4);
            if (x < -3 || x > Width + 3 || y < -3 || y > Height + 3) continue;
            double depth = (wave + fold + 2) / 4;
            byte alpha = (byte)((45 + 135 * depth) * (layer == 0 ? 1 : 0.32));
            float size = (float)(1.0 + depth * 0.8);
            var glow = new Sdl.Rect(x - 1.2f, y - 1.2f, size + 2.4f, size + 2.4f);
            Sdl.SDL_SetRenderDrawColor(_renderer, red, green, blue, (byte)(alpha / 9));
            Sdl.SDL_RenderFillRectF(_renderer, ref glow);
            var point = new Sdl.Rect(x, y, size, size);
            Sdl.SDL_SetRenderDrawColor(_renderer, red, green, blue, alpha);
            Sdl.SDL_RenderFillRectF(_renderer, ref point);
        }
        Sdl.SDL_RenderPresent(_renderer);
    }

    private static InvalidOperationException Error(string message) =>
        new($"{message}: {Marshal.PtrToStringUTF8(Sdl.SDL_GetError())}");

    public void Dispose()
    {
        if (_renderer != IntPtr.Zero) Sdl.SDL_DestroyRenderer(_renderer);
        if (_window != IntPtr.Zero) Sdl.SDL_DestroyWindow(_window);
        if (_initialized) Sdl.SDL_Quit();
        _renderer = _window = IntPtr.Zero;
        _initialized = false;
        GC.KeepAlive(_hitTest);
    }

    private static class Sdl
    {
        private const string Library = "libSDL2-2.0.so.0";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int HitTest(IntPtr window, IntPtr point, IntPtr data);

        // SDL2's event union is 56 bytes; keysym.sym is at offset 20.
        [StructLayout(LayoutKind.Explicit, Size = 56)]
        internal struct Event
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(12)] public byte WindowEvent;
            [FieldOffset(13)] public byte Repeat;
            [FieldOffset(20)] public int KeyCode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect(float x, float y, float width, float height)
        {
            public float X = x, Y = y, Width = width, Height = height;
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_Init(uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetError();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int x, int y, int w, int h, uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetWindowOpacity(IntPtr window, float opacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetWindowHitTest(IntPtr window, HitTest callback, IntPtr data);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_CreateRenderer(IntPtr window, int index, uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_RenderSetLogicalSize(IntPtr renderer, int w, int h);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetRenderDrawBlendMode(IntPtr renderer, int mode);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetRenderDrawColor(IntPtr renderer, byte r, byte g, byte b, byte a);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_RenderClear(IntPtr renderer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_RenderFillRectF(IntPtr renderer, ref Rect rect);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_RenderPresent(IntPtr renderer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_PollEvent(out Event input);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_DestroyRenderer(IntPtr renderer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_DestroyWindow(IntPtr window);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_Quit();
    }
}
