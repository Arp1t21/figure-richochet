namespace Ricochet;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--selftest"))
            return SelfTest.Run();
        if (args.Length == 2 && args[0] == "--shots")
            return Shots(args[1]);
        if (args.Contains("--bench"))
            return Bench();

        // страховочная сетка: непойманная ошибка пишется в error.log рядом с игрой
        Application.ThreadException += (s, e) => LogAndShow(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { File.WriteAllText(LogPath, e.ExceptionObject.ToString()); } catch { }
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new UI.MainForm());
        return 0;
    }

    public static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");

    private static void LogAndShow(Exception ex)
    {
        try { File.WriteAllText(LogPath, ex.ToString()); } catch { }
        MessageBox.Show("Произошла ошибка, игра продолжит работу.\nПодробности: " + Program.LogPath,
            "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Замер производительности: пост-обработка кадра и полный цикл «симуляция + рендер».</summary>
    private static int Bench()
    {
        ApplicationConfiguration.Initialize();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var bmp = new Bitmap(1280, 720, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.FromArgb(20, 24, 34));
        UI.Fx.Apply(bmp, 0); // прогрев
        sw.Restart();
        const int n = 60;
        for (int i = 0; i < n; i++) UI.Fx.Apply(bmp, i);
        Console.WriteLine($"Fx.Apply:   {sw.Elapsed.TotalMilliseconds / n:0.00} ms/кадр");

        var canvas = new UI.GameCanvas { Size = new Size(1280, 720) };
        canvas.LoadLevel(Core.BuiltinLevels.All()[0]);
        canvas.Sim.AddBall(new Core.Vec2(640, 360), new Core.Vec2(600, 300), 12);
        var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        using var pe = new PaintEventArgs(Graphics.FromImage(bmp), new Rectangle(0, 0, 1280, 720));
        sw.Restart();
        for (int i = 0; i < n; i++)
        {
            canvas.Sim.Step(1.0 / 60);
            onPaint.Invoke(canvas, new object[] { pe });
        }
        Console.WriteLine($"Симуляция+рендер: {sw.Elapsed.TotalMilliseconds / n:0.00} ms/кадр (бюджет 16.7)");

        // постановка шаров в режиме уровней: ловим максимальный кадр (фриз при клике?)
        {
            var fsw = System.Diagnostics.Stopwatch.StartNew();
            double maxFrame = 0, sum = 0;
            int frames = n * 3;
            for (int i = 0; i < frames; i++)
            {
                double t0 = fsw.Elapsed.TotalMilliseconds;
                if (i % 30 == 0) // раз в полсекунды — постановка шара
                    canvas.Sim.AddBall(new Core.Vec2(200 + (i % 5) * 40, 200 + (i % 3) * 60), new Core.Vec2(600, 300), 12);
                canvas.Sim.Step(1.0 / 60);
                onPaint.Invoke(canvas, new object[] { pe });
                double f = fsw.Elapsed.TotalMilliseconds - t0;
                sum += f;
                if (f > maxFrame) maxFrame = f;
            }
            Console.WriteLine($"Постановка шаров: среднее {sum / frames:0.00} ms/кадр, максимум {maxFrame:0.00} ms (фриз?)");
        }

        // волновой фронт: 1440 лучей в эллипсе (сетка стен + программный рендер)
        var wave = new UI.GameCanvas { Size = new Size(1280, 720), WaveMode = true, WaveSource = new Core.Vec2(640, 360) };
        var wlvl = new Core.LevelData();
        var poly = new Core.PolyData { Closed = true };
        for (int i = 0; i < 96; i++)
        {
            double a = i * 2 * Math.PI / 96;
            poly.Pts.Add(new Core.Vec2(640 + Math.Cos(a) * 430, 360 + Math.Sin(a) * 270));
        }
        wlvl.Walls.Add(poly);
        wave.LoadLevel(wlvl);
        wave.Sim.BallCollisions = false;
        for (int i = 0; i < 1440; i++)
        {
            double a = i * 2 * Math.PI / 1440;
            wave.Sim.AddBall(new Core.Vec2(640, 360), new Core.Vec2(Math.Cos(a), Math.Sin(a)) * 320, 3, (int)(i * 360.0 / 1440));
        }
        sw.Restart();
        for (int i = 0; i < n; i++)
        {
            wave.Sim.Step(1.0 / 60);
            onPaint.Invoke(wave, new object[] { pe });
        }
        Console.WriteLine($"Волна 1440 лучей: {sw.Elapsed.TotalMilliseconds / n:0.00} ms/кадр");

        // 5760 лучей — предельная плотность
        wave.Sim.Balls.Clear();
        for (int i = 0; i < 5760; i++)
        {
            double a = i * 2 * Math.PI / 5760;
            wave.Sim.AddBall(new Core.Vec2(640, 360), new Core.Vec2(Math.Cos(a), Math.Sin(a)) * 320, 3, (int)(i * 360.0 / 5760));
        }
        sw.Restart();
        for (int i = 0; i < n; i++)
        {
            wave.Sim.Step(1.0 / 60);
            onPaint.Invoke(wave, new object[] { pe });
        }
        Console.WriteLine($"Волна 5760 лучей: {sw.Elapsed.TotalMilliseconds / n:0.00} ms/кадр");
        return 0;
    }

    /// <summary>Отрисовка окна со всеми режимами в PNG — для визуальной проверки
    /// дизайна. Окно на мгновение показывается далеко за пределами экрана: без
    /// этого DrawToBitmap не создаёт дескрипторы дочерних контролов и рисует
    /// пустоту. Дополнительно снимается кадр в масштабе 150% — проверка, что
    /// панели инструментов не обрезают содержимое при высоком DPI.</summary>
    private static int Shots(string dir)
    {
        ApplicationConfiguration.Initialize();
        Directory.CreateDirectory(dir);
        using var shell = new UI.MainForm();
        shell.StartPosition = FormStartPosition.Manual;
        shell.Location = new Point(-20000, -20000);
        shell.Show();
        Application.DoEvents();
        System.Threading.Thread.Sleep(150);
        Application.DoEvents();
        Shot(shell, Path.Combine(dir, "play.png"));

        shell.ShowView(1);
        Application.DoEvents();
        System.Threading.Thread.Sleep(150);
        Application.DoEvents();
        Shot(shell, Path.Combine(dir, "editor.png"));

        shell.ShowView(2);
        Application.DoEvents();
        System.Threading.Thread.Sleep(150);
        Application.DoEvents();
        Shot(shell, Path.Combine(dir, "wave.png"));

        shell.ShowView(3);
        Application.DoEvents();
        System.Threading.Thread.Sleep(150);
        Application.DoEvents();
        Shot(shell, Path.Combine(dir, "rotor.png"));

        // второй уровень ротора — спираль из 200 кругов
        if (shell.ActiveView is UI.RotorView rv2)
        {
            rv2.ShowLevel(1);
            Application.DoEvents();
            System.Threading.Thread.Sleep(150);
            Application.DoEvents();
            Shot(shell, Path.Combine(dir, "rotor2.png"));
        }

        // имитация 150%: контролы увеличиваются, Toolbar пересчитывает центрирование
        shell.Scale(new SizeF(1.5f, 1.5f));
        Application.DoEvents();
        System.Threading.Thread.Sleep(150);
        Application.DoEvents();
        Shot(shell, Path.Combine(dir, "wave150.png"));
        shell.Close();
        return 0;
    }

    private static void Shot(Form f, string path)
    {
        // снимаем окно целиком (с рамкой) и вырезаем клиентскую область —
        // иначе DrawToBitmap вписывает рамку в клиентский размер и сдвигает контент
        using var full = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(full, new Rectangle(0, 0, f.Width, f.Height));
        var scrClient = f.PointToScreen(Point.Empty);
        var off = new Point(scrClient.X - f.Left, scrClient.Y - f.Top);
        using var client = full.Clone(new Rectangle(off, f.ClientSize), full.PixelFormat);
        client.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("shot: " + path);
    }
}
