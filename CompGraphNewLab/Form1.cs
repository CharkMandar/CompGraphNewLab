using System;
using System.Drawing;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace CompGraphNewLab
{
    public partial class Form1 : Form
    {
        private GLControl _glControl;
        private System.Windows.Forms.Timer _animationTimer;

        // Переменные для движения
        private double _journeyTime;
        private float _boxX, _boxY, _boxZ;
        private int _currentStage;  // 0=подъем, 1=выезд к краю, 2=вращение, 3=спуск по спирали, 4=возврат на старт

        // Параметры движений
        private float _liftSpeed = 1.2f;
        private float _liftHeight = 3.5f;
        private float _carouselRadius = 1.8f;
        private float _carouselAngularSpeed = 1.6f;
        private float _spiralRadius = 1.8f;
        private float _spiralAngularSpeed = 2.2f;
        private float _spiralDescentSpeed = 0.8f;
        private float _stage2Duration = 1.2f;
        private float _stage3Duration = 3.8f;
        private float _returnSpeed = 1.0f;

        private float _stage1Duration;
        private float _stage4Duration;
        private float _stage5Duration;
        private int _spiralDisplayList = -1;

        private DateTime _lastUpdateTime = DateTime.Now;

        // --- ПЕРЕМЕННЫЕ ДЛЯ УПРАВЛЕНИЯ КАМЕРОЙ ---
        private float _cameraDistance = 7.0f;
        private float _cameraAngleX = 30.0f;
        private float _cameraAngleY = 25.0f;
        private Point _lastMousePos;
        private bool _isMouseDragging = false;

        // --- ПЕРЕМЕННЫЕ ДЛЯ УПРАВЛЕНИЯ АНИМАЦИЕЙ ---
        private enum AnimationMode { Auto, Paused, Step }
        private AnimationMode _currentMode = AnimationMode.Auto;
        private bool _stepRequested = false;
        private int _stepStageToExecute = -1;
        private double _savedTime = 0;
        private bool _isPausedRandom = false;

        // --- ПОЗИЦИИ И ЦВЕТА ИСТОЧНИКОВ СВЕТА ---
        private float[][] _lightPositions = new float[][]
        {
            new float[] { 3.0f, 4.0f, 2.0f, 1.0f },  // Свет 1: справа-сверху (белый)
            new float[] { -2.0f, 3.0f, 3.0f, 1.0f }, // Свет 2: слева-сверху (теплый)
            new float[] { 1.0f, 1.0f, -3.0f, 1.0f }   // Свет 3: спереди-снизу (холодный)
        };

        private float[][] _lightColors = new float[][]
        {
            new float[] { 1.0f, 1.0f, 1.0f, 1.0f },    // Белый свет
            new float[] { 1.0f, 0.7f, 0.4f, 1.0f },    // Теплый (оранжевый)
            new float[] { 0.4f, 0.6f, 1.0f, 1.0f }     // Холодный (голубой)
        };

        private int _boxTextureId = -1;
        private int _platformTextureId = -1;

        public Form1()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.ResizeRedraw |
                          ControlStyles.OptimizedDoubleBuffer, true);

            InitializeComponent();

            _stage1Duration = _liftHeight / _liftSpeed + 0.3f;
            _stage4Duration = _liftHeight / _spiralDescentSpeed;
            _stage5Duration = _carouselRadius / _returnSpeed;

            this.Text = "Механическая лабораторная: Транспортный аттракцион (3 источника света)";
            this.ClientSize = new Size(900, 700);
            this.StartPosition = FormStartPosition.CenterScreen;

            _glControl = new GLControl();
            _glControl.Dock = DockStyle.Fill;
            _glControl.Load += GlControl_Load;
            _glControl.Paint += GlControl_Paint;
            _glControl.Resize += GlControl_Resize;

            _glControl.MouseDown += GlControl_MouseDown;
            _glControl.MouseUp += GlControl_MouseUp;
            _glControl.MouseMove += GlControl_MouseMove;
            _glControl.MouseWheel += GlControl_MouseWheel;

            this.Controls.Add(_glControl);

            // --- СОЗДАНИЕ КНОПОК УПРАВЛЕНИЯ ---
            CreateControlButtons();

            _animationTimer = new System.Windows.Forms.Timer();
            _animationTimer.Interval = 16;
            _animationTimer.Tick += AnimationTimer_Tick;
            _animationTimer.Start();
        }

        private void CreateControlButtons()
        {
            // Панель для кнопок (прозрачный фон)
            Panel buttonPanel = new Panel();
            buttonPanel.Dock = DockStyle.Top;
            buttonPanel.Height = 45;
            buttonPanel.BackColor = Color.FromArgb(200, 30, 30, 40);
            buttonPanel.Padding = new Padding(10);

            // Кнопка "Авто"
            Button btnAuto = new Button();
            btnAuto.Text = "▶ Продолжить";
            btnAuto.Size = new Size(100, 35);
            btnAuto.Location = new Point(10, 5);
            btnAuto.BackColor = Color.FromArgb(60, 90, 60);
            btnAuto.ForeColor = Color.White;
            btnAuto.FlatStyle = FlatStyle.Flat;
            btnAuto.FlatAppearance.BorderColor = Color.LightGreen;
            btnAuto.Click += (s, e) => {
                _currentMode = AnimationMode.Auto;
                _isPausedRandom = false;
                _savedTime = 0;
                _stepStageToExecute = -1;
            };

            // Кнопка "Пауза"
            Button btnPause = new Button();
            btnPause.Text = "⏸ ПАУЗА (случайно)";
            btnPause.Size = new Size(140, 35);
            btnPause.Location = new Point(120, 5);
            btnPause.BackColor = Color.FromArgb(90, 60, 60);
            btnPause.ForeColor = Color.White;
            btnPause.FlatStyle = FlatStyle.Flat;
            btnPause.Click += (s, e) => {
                if (_currentMode != AnimationMode.Paused)
                {
                    _currentMode = AnimationMode.Paused;
                    _isPausedRandom = true;
                    // Сохраняем текущее время, чтобы при возобновлении всё продолжилось
                    _savedTime = _journeyTime;
                }
            };

            // Кнопка "Далее (1 этап)"
            Button btnNext = new Button();
            btnNext.Text = "⏩ ДАЛЕЕ (1 этап)";
            btnNext.Size = new Size(140, 35);
            btnNext.Location = new Point(270, 5);
            btnNext.BackColor = Color.FromArgb(60, 60, 90);
            btnNext.ForeColor = Color.White;
            btnNext.FlatStyle = FlatStyle.Flat;
            btnNext.Click += (s, e) => {
                if (_currentMode == AnimationMode.Paused || _currentMode == AnimationMode.Step)
                {
                    // Определяем, какой этап сейчас нужно проиграть
                    _stepStageToExecute = _currentStage;
                    _currentMode = AnimationMode.Step;
                }
                else
                {
                    // Если не на паузе - сначала останавливаем
                    _currentMode = AnimationMode.Paused;
                    _savedTime = _journeyTime;
                    _stepStageToExecute = _currentStage;
                    _currentMode = AnimationMode.Step;
                }
            };

            // Кнопка "Сброс"
            Button btnReset = new Button();
            btnReset.Text = "⟳ СБРОС";
            btnReset.Size = new Size(90, 35);
            btnReset.Location = new Point(420, 5);
            btnReset.BackColor = Color.FromArgb(70, 70, 80);
            btnReset.ForeColor = Color.White;
            btnReset.FlatStyle = FlatStyle.Flat;
            btnReset.Click += (s, e) => {
                _journeyTime = 0;
                _boxX = 0; _boxY = 0; _boxZ = 0;
                _currentStage = 0;
                _currentMode = AnimationMode.Auto;
                _stepStageToExecute = -1;
                _isPausedRandom = false;
            };

            buttonPanel.Controls.Add(btnAuto);
            buttonPanel.Controls.Add(btnPause);
            buttonPanel.Controls.Add(btnNext);
            buttonPanel.Controls.Add(btnReset);

            this.Controls.Add(buttonPanel);
        }

        private void GlControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isMouseDragging = true;
                _lastMousePos = e.Location;
                _glControl.Cursor = Cursors.SizeAll;
            }
        }

        private void GlControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isMouseDragging = false;
                _glControl.Cursor = Cursors.Default;
            }
        }

        private void GlControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMouseDragging)
            {
                float deltaX = e.X - _lastMousePos.X;
                float deltaY = e.Y - _lastMousePos.Y;

                _cameraAngleX += deltaX * 0.5f;
                _cameraAngleY += deltaY * 0.3f;
                _cameraAngleY = Math.Max(-80.0f, Math.Min(80.0f, _cameraAngleY));

                _lastMousePos = e.Location;
                _glControl.Invalidate();
            }
        }

        private void GlControl_MouseWheel(object sender, MouseEventArgs e)
        {
            _cameraDistance -= e.Delta * 0.01f;
            _cameraDistance = Math.Max(2.0f, Math.Min(15.0f, _cameraDistance));
            _glControl.Invalidate();
        }

        private Matrix4 GetCameraMatrix()
        {
            float radAngleX = _cameraAngleX * (float)(Math.PI / 180.0);
            float radAngleY = _cameraAngleY * (float)(Math.PI / 180.0);

            float camX = (float)(_cameraDistance * Math.Cos(radAngleY) * Math.Sin(radAngleX));
            float camY = (float)(_cameraDistance * Math.Sin(radAngleY));
            float camZ = (float)(_cameraDistance * Math.Cos(radAngleY) * Math.Cos(radAngleX));

            return Matrix4.LookAt(camX, camY + 1.5f, camZ, 0.0f, 1.5f, 0.0f, 0.0f, 1.0f, 0.0f);
        }

        private void SetupLighting()
        {
            // Включаем три источника света
            GL.Enable(EnableCap.Light0);
            GL.Enable(EnableCap.Light1);
            GL.Enable(EnableCap.Light2);

            // Настройка Light0 (белый, справа-сверху)
            GL.Light(LightName.Light0, LightParameter.Position, _lightPositions[0]);
            GL.Light(LightName.Light0, LightParameter.Diffuse, _lightColors[0]);
            GL.Light(LightName.Light0, LightParameter.Specular, _lightColors[0]);
            GL.Light(LightName.Light0, LightParameter.Ambient, new float[] { 0.2f, 0.2f, 0.2f, 1.0f });

            // Настройка Light1 (теплый, слева-сверху)
            GL.Light(LightName.Light1, LightParameter.Position, _lightPositions[1]);
            GL.Light(LightName.Light1, LightParameter.Diffuse, _lightColors[1]);
            GL.Light(LightName.Light1, LightParameter.Specular, _lightColors[1]);

            // Настройка Light2 (холодный, спереди-снизу)
            GL.Light(LightName.Light2, LightParameter.Position, _lightPositions[2]);
            GL.Light(LightName.Light2, LightParameter.Diffuse, _lightColors[2]);
            GL.Light(LightName.Light2, LightParameter.Specular, _lightColors[2]);
        }

        private void GlControl_Load(object sender, EventArgs e)
        {
            _glControl.MakeCurrent();

            GL.ClearColor(0.08f, 0.08f, 0.12f, 1.0f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Lighting);
            GL.Enable(EnableCap.ColorMaterial);
            GL.ColorMaterial(MaterialFace.FrontAndBack, ColorMaterialParameter.AmbientAndDiffuse);

            // Настраиваем три источника света
            SetupLighting();

            // Загрузка текстур
            _boxTextureId = LoadTexture("box_texture.jpg");
            _platformTextureId = LoadTexture("platform_texture.jpg");

            if (_boxTextureId == -1) _boxTextureId = CreateProgrammaticTexture();
            if (_platformTextureId == -1) _platformTextureId = CreateProgrammaticTexture();

            // Включаем текстурирование
            GL.Enable(EnableCap.Texture2D);

            _spiralDisplayList = GL.GenLists(1);
            GL.NewList(_spiralDisplayList, ListMode.Compile);
            DrawSpiralRail();
            GL.EndList();

            SetupViewport();
        }

        private void SetupViewport()
        {
            _glControl.MakeCurrent();
            int width = _glControl.Width;
            int height = _glControl.Height;
            if (height == 0) height = 1;

            GL.Viewport(0, 0, width, height);
            GL.MatrixMode(MatrixMode.Projection);
            Matrix4 perspective = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45.0f),
                (float)width / (float)height,
                0.1f,
                100.0f);
            GL.LoadMatrix(ref perspective);

            GL.MatrixMode(MatrixMode.Modelview);
        }

        private void GlControl_Resize(object sender, EventArgs e)
        {
            SetupViewport();
            _glControl.Invalidate();
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            float deltaTime = (float)(now - _lastUpdateTime).TotalSeconds;
            if (deltaTime > 0.1f) deltaTime = 0.1f;
            _lastUpdateTime = now;

            // Обработка режимов анимации
            if (_currentMode == AnimationMode.Auto)
            {
                UpdateMovement(deltaTime);
            }
            else if (_currentMode == AnimationMode.Paused)
            {
                // Ничего не делаем, время заморожено
                if (_isPausedRandom && _savedTime == 0)
                {
                    _savedTime = _journeyTime;
                }
            }
            else if (_currentMode == AnimationMode.Step && _stepStageToExecute >= 0)
            {
                // Проигрываем один полный этап
                float stageStartTime = 0;
                float stageDuration = 0;

                switch (_stepStageToExecute)
                {
                    case 0: stageDuration = _stage1Duration; break;
                    case 1: stageDuration = _stage2Duration; break;
                    case 2: stageDuration = _stage3Duration; break;
                    case 3: stageDuration = _stage4Duration; break;
                    case 4: stageDuration = _stage5Duration; break;
                }

                // Сохраняем начальное время этапа
                double stepStartTime = _journeyTime;
                bool stageCompleted = false;

                // Временно форсируем прохождение этапа
                double targetTime = stepStartTime + stageDuration;

                while (_journeyTime < targetTime && !stageCompleted)
                {
                    _journeyTime += deltaTime;
                    if (_journeyTime >= targetTime)
                    {
                        _journeyTime = targetTime;
                        stageCompleted = true;
                    }
                    UpdateMovement(0); // Обновляем позицию без изменения времени
                }

                _stepStageToExecute = -1;
                _currentMode = AnimationMode.Paused;
                _isPausedRandom = true;
                _savedTime = _journeyTime;
            }

            _glControl.Invalidate();
        }

        private void UpdateMovement(float deltaTime)
        {
            if (_currentMode != AnimationMode.Paused)
            {
                _journeyTime += deltaTime;
            }

            // Временные метки этапов
            double stage1End = _stage1Duration;
            double stage2End = stage1End + _stage2Duration;
            double stage3End = stage2End + _stage3Duration;
            double stage4End = stage3End + _stage4Duration;
            double stage5End = stage4End + _stage5Duration;

            if (_journeyTime < stage1End)
            {
                _currentStage = 0;
                float t = (float)_journeyTime;
                _boxY = _liftSpeed * t;
                _boxX = 0.0f;
                _boxZ = 0.0f;
            }
            else if (_journeyTime < stage2End)
            {
                _currentStage = 1;
                float t = (float)(_journeyTime - stage1End);
                float progress = t / _stage2Duration;
                _boxX = _carouselRadius * progress;
                _boxZ = 0.0f;
                _boxY = _liftHeight + 0.3f;
            }
            else if (_journeyTime < stage3End)
            {
                _currentStage = 2;
                float t = (float)(_journeyTime - stage2End);
                float angle = _carouselAngularSpeed * t;
                _boxX = _carouselRadius * (float)Math.Cos(angle);
                _boxZ = _carouselRadius * (float)Math.Sin(angle);
                _boxY = _liftHeight + 0.3f;
            }
            else if (_journeyTime < stage4End)
            {
                _currentStage = 3;
                float t = (float)(_journeyTime - stage3End);
                float angle = _spiralAngularSpeed * t;
                _boxX = _spiralRadius * (float)Math.Cos(angle);
                _boxZ = _spiralRadius * (float)Math.Sin(angle);
                _boxY = _liftHeight + 0.3f - _spiralDescentSpeed * t;
            }
            else if (_journeyTime < stage5End)
            {
                _currentStage = 4;
                float t = (float)(_journeyTime - stage4End);
                float progress = 1.0f - t / _stage5Duration;
                float radius = _spiralRadius * progress;
                float angle = _spiralAngularSpeed * _stage4Duration;
                _boxX = radius * (float)Math.Cos(angle);
                _boxZ = radius * (float)Math.Sin(angle);
                _boxY = 0.0f;
            }
            else
            {
                _journeyTime = 0;
                _boxX = 0; _boxY = 0; _boxZ = 0;
                _currentStage = 0;
            }
        }

        private void GlControl_Paint(object sender, PaintEventArgs e)
        {
            _glControl.MakeCurrent();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.MatrixMode(MatrixMode.Modelview);
            Matrix4 view = GetCameraMatrix();
            GL.LoadMatrix(ref view);

            // Обновляем позиции источников света для динамического эффекта
            UpdateLightPositions();

            // Декорации
            DrawGroundPlane();
            DrawLiftColumn();
            //DrawCarouselPlatform();
            DrawCarouselPlatformVertexArray();
            DrawSpiralSupport();

            if (_spiralDisplayList != -1)
            {
                GL.CallList(_spiralDisplayList);
            }

            // Рисуем платформу под коробкой
            DrawMovingPlatform();

            // Коробка
            GL.PushMatrix();
            GL.Translate(_boxX, _boxY, _boxZ);

            if (_currentStage == 2)
            {
                float angle = _carouselAngularSpeed * (float)(_journeyTime - _stage1Duration - _stage2Duration);
                GL.Rotate(angle * 57.29578f, 0.0f, 1.0f, 0.0f);
            }
            else if (_currentStage == 3)
            {
                float angle = _spiralAngularSpeed * (float)(_journeyTime - _stage1Duration - _stage2Duration - _stage3Duration);
                GL.Rotate(angle * 57.29578f, 0.0f, 1.0f, 0.0f);
            }

            DrawBox(0.35f);
            GL.PopMatrix();

            _glControl.SwapBuffers();
        }

        /// <summary>
        /// Анимирует источники света для динамического эффекта
        /// </summary>
        private void UpdateLightPositions()
        {
            float time = (float)_journeyTime;

            // Light0 движется по кругу
            _lightPositions[0][0] = 3.0f + (float)Math.Sin(time * 0.8f) * 1.5f;
            _lightPositions[0][2] = 2.0f + (float)Math.Cos(time * 0.6f) * 2.0f;
            GL.Light(LightName.Light0, LightParameter.Position, _lightPositions[0]);

            // Light1 пульсирует по яркости
            float intensity = 0.7f + (float)Math.Sin(time * 1.2f) * 0.3f;
            GL.Light(LightName.Light1, LightParameter.Diffuse, new float[] { intensity, intensity * 0.7f, intensity * 0.4f, 1.0f });

            // Light2 движется вверх-вниз
            _lightPositions[2][1] = 1.0f + (float)Math.Sin(time * 1.5f) * 1.5f;
            GL.Light(LightName.Light2, LightParameter.Position, _lightPositions[2]);
        }

        private void DrawMovingPlatform()
        {
            GL.PushMatrix();
            GL.Translate(_boxX, _boxY - 0.4f, _boxZ);

            // Привязываем текстуру платформы
            GL.BindTexture(TextureTarget.Texture2D, _platformTextureId);
            GL.Color3(1.0f, 1.0f, 1.0f);

            int segments = 24;
            float radius = 0.55f;
            float yPos = 0.0f;

            GL.Begin(PrimitiveType.Triangles);

            for (int i = 0; i < segments; i++)
            {
                float angle1 = (float)(i * 2 * Math.PI / segments);
                float angle2 = (float)((i + 1) * 2 * Math.PI / segments);

                float x1 = radius * (float)Math.Cos(angle1);
                float z1 = radius * (float)Math.Sin(angle1);
                float x2 = radius * (float)Math.Cos(angle2);
                float z2 = radius * (float)Math.Sin(angle2);

                // UV-координаты: центр текстуры (0.5, 0.5) для центра диска
                // края — по кругу
                float u1 = 0.5f + 0.5f * (float)Math.Cos(angle1);
                float v1 = 0.5f + 0.5f * (float)Math.Sin(angle1);
                float u2 = 0.5f + 0.5f * (float)Math.Cos(angle2);
                float v2 = 0.5f + 0.5f * (float)Math.Sin(angle2);

                // Треугольник 1: центр → вершина1 → вершина2
                GL.TexCoord2(0.5f, 0.5f); GL.Vertex3(0.0f, yPos, 0.0f);
                GL.TexCoord2(u1, v1); GL.Vertex3(x1, yPos, z1);
                GL.TexCoord2(u2, v2); GL.Vertex3(x2, yPos, z2);
            }

            GL.End();

            // Ободок платформы (можно оставить без текстуры, просто цветной)
            GL.Disable(EnableCap.Texture2D);
            GL.Color3(0.6f, 0.7f, 0.4f);
            GL.LineWidth(2.0f);
            GL.Begin(PrimitiveType.LineLoop);
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(i * 2 * Math.PI / segments);
                float x = radius * (float)Math.Cos(angle);
                float z = radius * (float)Math.Sin(angle);
                GL.Vertex3(x, 0.05f, z);
            }
            GL.End();
            GL.Enable(EnableCap.Texture2D);

            GL.PopMatrix();
        }

        private void DrawGroundPlane()
        {
            GL.PushMatrix();
            GL.Translate(0.0f, -0.6f, 0.0f);
            GL.Color4(0.3f, 0.3f, 0.4f, 0.5f);
            GL.Begin(PrimitiveType.Quads);
            GL.Vertex3(-4.0f, 0.0f, -4.0f);
            GL.Vertex3(4.0f, 0.0f, -4.0f);
            GL.Vertex3(4.0f, 0.0f, 4.0f);
            GL.Vertex3(-4.0f, 0.0f, 4.0f);
            GL.End();
            GL.PopMatrix();
        }

        private void DrawLiftColumn()
        {
            GL.Color3(0.5f, 0.5f, 0.6f);
            GL.Begin(PrimitiveType.Lines);
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                {
                    GL.Vertex3(i * 0.4f, 0.0f, j * 0.4f);
                    GL.Vertex3(i * 0.4f, _liftHeight, j * 0.4f);
                }
            GL.End();
        }

        private void DrawCarouselPlatform()
        {
            GL.Color4(0.4f, 0.5f, 0.4f, 0.7f);
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex3(0.0f, _liftHeight - 0.05f, 0.0f);
            int segments = 30;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(i * 2 * Math.PI / segments);
                float x = _carouselRadius * (float)Math.Cos(angle);
                float z = _carouselRadius * (float)Math.Sin(angle);
                GL.Vertex3(x, _liftHeight - 0.05f, z);
            }
            GL.End();

            GL.Color3(0.6f, 0.7f, 0.5f);
            GL.LineWidth(2.0f);
            GL.Begin(PrimitiveType.LineLoop);
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(i * 2 * Math.PI / segments);
                float x = _carouselRadius * (float)Math.Cos(angle);
                float z = _carouselRadius * (float)Math.Sin(angle);
                GL.Vertex3(x, _liftHeight - 0.03f, z);
            }
            GL.End();
        }

        private void DrawSpiralSupport()
        {
            GL.Color3(0.6f, 0.6f, 0.7f);
            GL.LineWidth(2.0f);
            GL.Begin(PrimitiveType.Lines);
            GL.Vertex3(0.0f, 0.0f, 0.0f);
            GL.Vertex3(0.0f, _liftHeight, 0.0f);
            GL.End();
            GL.LineWidth(1.0f);
        }

        private void DrawSpiralRail()
        {
            GL.Color3(0.8f, 0.7f, 0.3f);
            GL.LineWidth(2.5f);
            GL.Begin(PrimitiveType.LineStrip);
            int steps = 100;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float angle = _spiralAngularSpeed * _stage4Duration * t;
                float y = _liftHeight - _spiralDescentSpeed * _stage4Duration * t;
                float x = _spiralRadius * (float)Math.Cos(angle);
                float z = _spiralRadius * (float)Math.Sin(angle);
                GL.Vertex3(x, y, z);
            }
            GL.End();
            GL.LineWidth(1.0f);
        }

        private void DrawBox(float size)
        {
            // Привязываем текстуру коробки
            GL.BindTexture(TextureTarget.Texture2D, _boxTextureId);
            GL.Color3(1.0f, 1.0f, 1.0f); // Белый цвет — текстура отображается полностью

            float s = size;

            GL.Begin(PrimitiveType.Triangles);

            // === ПЕРЕДНЯЯ ГРАНЬ (Z = +s) ===
            // Нижний левый угол текстуры (0,0) → нижний левый угол грани
            GL.TexCoord2(0, 0); GL.Vertex3(-s, -s, s);
            GL.TexCoord2(1, 0); GL.Vertex3(s, -s, s);
            GL.TexCoord2(1, 1); GL.Vertex3(s, s, s);

            GL.TexCoord2(0, 0); GL.Vertex3(-s, -s, s);
            GL.TexCoord2(1, 1); GL.Vertex3(s, s, s);
            GL.TexCoord2(0, 1); GL.Vertex3(-s, s, s);

            // === ЗАДНЯЯ ГРАНЬ (Z = -s) ===
            GL.TexCoord2(0, 0); GL.Vertex3(-s, -s, -s);
            GL.TexCoord2(1, 1); GL.Vertex3(s, s, -s);
            GL.TexCoord2(1, 0); GL.Vertex3(s, -s, -s);

            GL.TexCoord2(0, 0); GL.Vertex3(-s, -s, -s);
            GL.TexCoord2(0, 1); GL.Vertex3(-s, s, -s);
            GL.TexCoord2(1, 1); GL.Vertex3(s, s, -s);

            // === ЛЕВАЯ ГРАНЬ (X = -s) ===
            GL.TexCoord2(0, 0); GL.Vertex3(-s, -s, -s);
            GL.TexCoord2(1, 1); GL.Vertex3(-s, s, s);
            GL.TexCoord2(1, 0); GL.Vertex3(-s, -s, s);

            GL.TexCoord2(0, 0); GL.Vertex3(-s, -s, -s);
            GL.TexCoord2(0, 1); GL.Vertex3(-s, s, -s);
            GL.TexCoord2(1, 1); GL.Vertex3(-s, s, s);

            // === ПРАВАЯ ГРАНЬ (X = +s) ===
            GL.TexCoord2(0, 0); GL.Vertex3(s, -s, -s);
            GL.TexCoord2(1, 0); GL.Vertex3(s, -s, s);
            GL.TexCoord2(1, 1); GL.Vertex3(s, s, s);

            GL.TexCoord2(0, 0); GL.Vertex3(s, -s, -s);
            GL.TexCoord2(1, 1); GL.Vertex3(s, s, s);
            GL.TexCoord2(0, 1); GL.Vertex3(s, s, -s);

            // === ВЕРХНЯЯ ГРАНЬ (Y = +s) ===
            GL.TexCoord2(0, 0); GL.Vertex3(-s, s, -s);
            GL.TexCoord2(1, 1); GL.Vertex3(s, s, s);
            GL.TexCoord2(1, 0); GL.Vertex3(s, s, -s);

            GL.TexCoord2(0, 0); GL.Vertex3(-s, s, -s);
            GL.TexCoord2(0, 1); GL.Vertex3(-s, s, s);
            GL.TexCoord2(1, 1); GL.Vertex3(s, s, s);

            // === НИЖНЯЯ ГРАНЬ (Y = -s) ===
            GL.TexCoord2(0, 0); GL.Vertex3(-s, -s, -s);
            GL.TexCoord2(1, 0); GL.Vertex3(s, -s, s);
            GL.TexCoord2(1, 1); GL.Vertex3(s, -s, -s);

            GL.TexCoord2(0, 0); GL.Vertex3(-s, -s, -s);
            GL.TexCoord2(0, 1); GL.Vertex3(-s, -s, s);
            GL.TexCoord2(1, 0); GL.Vertex3(s, -s, s);

            GL.End();
        }

        private void DrawCarouselPlatformVertexArray()
        {
            int segments = 30;
            float radius = _carouselRadius;
            float yPos = _liftHeight - 0.05f;

            // Количество вершин: (сегментов * 3 вершины на треугольник)
            int vertexCount = segments * 3;
            float[] vertices = new float[vertexCount * 3]; // x, y, z для каждой вершины

            for (int i = 0; i < segments; i++)
            {
                float angle1 = (float)(i * 2 * Math.PI / segments);
                float angle2 = (float)((i + 1) * 2 * Math.PI / segments);

                float x1 = radius * (float)Math.Cos(angle1);
                float z1 = radius * (float)Math.Sin(angle1);
                float x2 = radius * (float)Math.Cos(angle2);
                float z2 = radius * (float)Math.Sin(angle2);

                // Треугольник (центр, вершина1, вершина2)
                int idx = i * 9; // 9 координат на треугольник (3 вершины * 3 координаты)
                vertices[idx + 0] = 0; vertices[idx + 1] = yPos; vertices[idx + 2] = 0;
                vertices[idx + 3] = x1; vertices[idx + 4] = yPos; vertices[idx + 5] = z1;
                vertices[idx + 6] = x2; vertices[idx + 7] = yPos; vertices[idx + 8] = z2;
            }

            // Включаем режим работы с массивами
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.VertexPointer(3, VertexPointerType.Float, 0, vertices);
            GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);
            GL.DisableClientState(ArrayCap.VertexArray);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(900, 700);
            this.Name = "Form1";
            this.Text = "Лабораторка";
            this.ResumeLayout(false);
        }

        /// <summary>
        /// Загружает текстуру из файла и возвращает её ID
        /// </summary>
        private int LoadTexture(string filePath)
        {
            // Проверяем, существует ли файл
            if (!System.IO.File.Exists(filePath))
            {
                // Если нет файла — создаём программную текстуру (чтобы код работал всегда)
                return CreateProgrammaticTexture();
            }

            using (Bitmap bitmap = new Bitmap(filePath))
            {
                int textureId = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, textureId);

                // Получаем данные пикселей
                System.Drawing.Imaging.BitmapData data = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                // Передаём данные в видеопамять
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                    data.Width, data.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);

                bitmap.UnlockBits(data);

                // Настройка фильтрации (чтобы текстура не была пиксельной)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                // Повторение текстуры (если UV выходят за пределы 0-1)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

                return textureId;
            }
        }

        /// <summary>
        /// Создаёт простую цветную шахматную текстуру (если нет файлов)
        /// </summary>
        private int CreateProgrammaticTexture()
        {
            int size = 128;
            byte[] pixels = new byte[size * size * 4];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = (y * size + x) * 4;
                    // Шахматный узор 8x8 клеток
                    bool isWhite = ((x / 16) + (y / 16)) % 2 == 0;

                    if (isWhite)
                    {
                        pixels[index] = 200;     // B (синий)
                        pixels[index + 1] = 180; // G (зелёный)
                        pixels[index + 2] = 100; // R (красный)
                    }
                    else
                    {
                        pixels[index] = 100;     // B
                        pixels[index + 1] = 80;  // G
                        pixels[index + 2] = 40;  // R
                    }
                    pixels[index + 3] = 255;     // Alpha (непрозрачный)
                }
            }

            int textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                size, size, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            return textureId;
        }
    }
}