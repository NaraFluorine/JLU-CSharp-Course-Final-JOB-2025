using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;

namespace _55230620
{
    // DeepSeek API 请求和响应类
    public class DeepSeekMessage
    {
        public string role { get; set; }
        public string content { get; set; }
    }
    
    public class DeepSeekRequest
    {
        public string model { get; set; } = "deepseek-chat";
        public List<DeepSeekMessage> messages { get; set; }
        public bool stream { get; set; } = false;
    }
    
    public class DeepSeekChoice
    {
        public DeepSeekMessage message { get; set; }
    }
    
    public class DeepSeekResponse
    {
        public List<DeepSeekChoice> choices { get; set; }
    }
    
    public class TetrisGame : Form
    {
        // 游戏常量
        private const int BlockSize = 30; // 每个小方块的大小
        private const int BoardWidth = 10; // 游戏板宽度（格子数）
        private const int BoardHeight = 20; // 游戏板高度（格子数）
        private const int PanelPadding = 10; // 面板内边距

        // 游戏状态
        private int[,] gameBoard; // 游戏板状态，0表示空，其他值表示不同颜色的方块
        private int score = 0; // 游戏得分
        private bool isPaused = false; // 游戏是否暂停
        private bool isAIPredict = false; // 是否显示AI预测
        private int blockCount = 0; // 已下落的方块数量
        private DateTime lastFrameTime; // 上一帧的时间
        
        // DeepSeek API
        private string deepseekApiKey; // API密钥
        private readonly HttpClient httpClient = new HttpClient(); // HTTP客户端

        // 当前方块和下一个方块
        private Tetromino currentTetromino;
        private Tetromino nextTetromino;

        // UI组件
        private Panel gamePanel; // 游戏主面板
        private Panel nextBlockPanel; // 显示下一个方块的面板
        private Label scoreLabel; // 显示分数的标签
        private Label pauseLabel; // 显示暂停状态的标签

        // 游戏计时器
        private Timer gameTimer;

        // AI预测的最佳位置
        private List<Point> aiPredictedPositions = new List<Point>();

        public TetrisGame()
        {
            // 从环境变量读取API密钥
            deepseekApiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if (string.IsNullOrEmpty(deepseekApiKey))
            {
                MessageBox.Show("未找到DEEPSEEK_API_KEY环境变量，AI预测功能将不可用", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            
            InitializeComponents();
            InitializeGame();
        }

        private void InitializeComponents()
        {
            // 设置窗体属性
            this.Text = "俄罗斯方块";
            this.Size = new Size(600, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            this.KeyDown += TetrisGame_KeyDown;

            // 创建游戏面板
            gamePanel = new Panel
            {
                Location = new Point(PanelPadding, PanelPadding),
                Size = new Size(BoardWidth * BlockSize, BoardHeight * BlockSize),
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };
            gamePanel.Paint += GamePanel_Paint;

            // 创建下一个方块面板
            nextBlockPanel = new Panel
            {
                Location = new Point(gamePanel.Right + 20, PanelPadding),
                Size = new Size(5 * BlockSize, 5 * BlockSize),
                BackColor = Color.LightGray,
                BorderStyle = BorderStyle.FixedSingle
            };
            nextBlockPanel.Paint += NextBlockPanel_Paint;

            // 创建分数标签
            scoreLabel = new Label
            {
                Location = new Point(nextBlockPanel.Left, nextBlockPanel.Bottom + 30),
                Size = new Size(150, 30),
                Text = "分数: 0",
                Font = new Font("Arial", 14, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // 创建暂停标签
            pauseLabel = new Label
            {
                Location = new Point(nextBlockPanel.Left, scoreLabel.Bottom + 20),
                Size = new Size(150, 30),
                Text = "PAUSE",
                Font = new Font("Arial", 14, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };

            // 添加控件到窗体
            this.Controls.Add(gamePanel);
            this.Controls.Add(nextBlockPanel);
            this.Controls.Add(scoreLabel);
            this.Controls.Add(pauseLabel);

            // 创建游戏计时器
            gameTimer = new Timer
            {
                Interval = 16 // 约60FPS，用于平滑渲染，但下落速度由GameTimer_Tick中控制
            };
            gameTimer.Tick += GameTimer_Tick;
        }

        private void InitializeGame()
        {
            // 初始化游戏板
            gameBoard = new int[BoardHeight, BoardWidth];
            for (int i = 0; i < BoardHeight; i++)
            {
                for (int j = 0; j < BoardWidth; j++)
                {
                    gameBoard[i, j] = 0;
                }
            }

            // 创建第一个方块和下一个方块
            currentTetromino = CreateRandomTetromino();
            nextTetromino = CreateRandomTetromino();

            // 重置分数和方块计数
            score = 0;
            blockCount = 0;
            UpdateScoreLabel();
            
            // 初始化时间
            lastFrameTime = DateTime.Now;

            // 开始游戏计时器
            gameTimer.Interval = 16; // 约60FPS
            gameTimer.Start();
        }

        private void GamePanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // 绘制游戏板上的固定方块
            for (int i = 0; i < BoardHeight; i++)
            {
                for (int j = 0; j < BoardWidth; j++)
                {
                    if (gameBoard[i, j] != 0)
                    {
                        DrawBlock(g, j, i, GetColorFromValue(gameBoard[i, j]));
                    }
                }
            }

            // 绘制当前下落的方块
            if (currentTetromino != null && !isPaused)
            {
                foreach (Point p in currentTetromino.Blocks)
                {
                    int x = currentTetromino.Position.X + p.X;
                    int y = currentTetromino.Position.Y + p.Y;
                    if (x >= 0 && x < BoardWidth && y >= 0 && y < BoardHeight)
                    {
                        DrawBlock(g, x, y, currentTetromino.Color);
                    }
                }
            }

            // 绘制AI预测位置
            if (isAIPredict && aiPredictedPositions.Count > 0)
            {
                foreach (Point p in aiPredictedPositions)
                {
                    DrawBlock(g, p.X, p.Y, Color.White, true);
                }
            }
        }

        private void NextBlockPanel_Paint(object sender, PaintEventArgs e)
        {
            if (nextTetromino != null)
            {
                Graphics g = e.Graphics;
                g.Clear(Color.LightGray);

                // 计算居中位置
                int offsetX = (nextBlockPanel.Width - 4 * BlockSize) / 2;
                int offsetY = (nextBlockPanel.Height - 4 * BlockSize) / 2;

                // 绘制下一个方块
                foreach (Point p in nextTetromino.Blocks)
                {
                    int x = p.X + 1; // +1 是为了居中显示
                    int y = p.Y + 1;
                    DrawBlock(g, x, y, nextTetromino.Color, false, offsetX, offsetY);
                }
            }
        }

        private void DrawBlock(Graphics g, int x, int y, Color color, bool isAIPredict = false, int offsetX = 0, int offsetY = 0)
        {
            // 如果是AI预测，只改变背景色
            if (isAIPredict)
            {
                g.FillRectangle(new SolidBrush(color), x * BlockSize, y * BlockSize, BlockSize, BlockSize);
                return;
            }

            // 绘制方块的填充
            g.FillRectangle(new SolidBrush(color), offsetX + x * BlockSize, offsetY + y * BlockSize, BlockSize, BlockSize);

            // 绘制方块的边框
            g.DrawRectangle(new Pen(Color.Black), offsetX + x * BlockSize, offsetY + y * BlockSize, BlockSize, BlockSize);

            // 绘制方块的高光效果
            g.DrawLine(new Pen(Color.White), offsetX + x * BlockSize, offsetY + y * BlockSize, offsetX + x * BlockSize + BlockSize - 1, offsetY + y * BlockSize);
            g.DrawLine(new Pen(Color.White), offsetX + x * BlockSize, offsetY + y * BlockSize, offsetX + x * BlockSize, offsetY + y * BlockSize + BlockSize - 1);
        }

        private void GameTimer_Tick(object sender, EventArgs e)
        {
            if (isPaused)
                return;
                
            // 计算当前时间与上一帧的时间差
            TimeSpan elapsed = DateTime.Now - lastFrameTime;
            
            // 固定下落时间为1.2秒
            double frameTime = 1.2;
            
            // 如果经过的时间超过了固定帧时间，则下落一格
            if (elapsed.TotalSeconds >= frameTime)
            {
                MoveDown();
                lastFrameTime = DateTime.Now;
            }
            
            // 刷新游戏面板
            gamePanel.Invalidate();
        }

        private void TetrisGame_KeyDown(object sender, KeyEventArgs e)
        {
            if (isPaused && e.KeyCode != Keys.P && e.KeyCode != Keys.O)
            {
                return; // 如果游戏暂停，只响应P和O键
            }

            bool actionTaken = false;
            
            switch (e.KeyCode)
            {
                case Keys.A: // 左移
                    MoveLeft();
                    actionTaken = true;
                    break;
                case Keys.D: // 右移
                    MoveRight();
                    actionTaken = true;
                    break;
                case Keys.S: // 下移
                    MoveDown();
                    actionTaken = true;
                    break;
                case Keys.W: // 旋转
                    Rotate();
                    actionTaken = true;
                    break;
                case Keys.P: // 暂停/继续
                    TogglePause();
                    break;
                case Keys.O: // AI预测（仅在暂停状态下有效）
                    if (isPaused)
                    {
                        ToggleAIPredict();
                    }
                    break;
            }

            // 玩家操作会刷新游戏面板，但不会重置下落时间
            
            // 刷新游戏面板
            gamePanel.Invalidate();
        }

        private void MoveLeft()
        {
            currentTetromino.Position = new Point(currentTetromino.Position.X - 1, currentTetromino.Position.Y);
            if (!IsValidPosition())
            {
                currentTetromino.Position = new Point(currentTetromino.Position.X + 1, currentTetromino.Position.Y);
            }
        }

        private void MoveRight()
        {
            currentTetromino.Position = new Point(currentTetromino.Position.X + 1, currentTetromino.Position.Y);
            if (!IsValidPosition())
            {
                currentTetromino.Position = new Point(currentTetromino.Position.X - 1, currentTetromino.Position.Y);
            }
        }

        private void MoveDown()
        {
            currentTetromino.Position = new Point(currentTetromino.Position.X, currentTetromino.Position.Y + 1);
            if (!IsValidPosition())
            {
                currentTetromino.Position = new Point(currentTetromino.Position.X, currentTetromino.Position.Y - 1);
                LockTetromino();
                ClearLines();
                SpawnNewTetromino();
            }
        }

        private void Rotate()
        {
            currentTetromino.Rotate();
            if (!IsValidPosition())
            {
                // 如果旋转后位置无效，则旋转回去
                for (int i = 0; i < 3; i++)
                {
                    currentTetromino.Rotate(); // 旋转三次相当于逆时针旋转一次
                }
            }
        }

        private void TogglePause()
        {
            isPaused = !isPaused;
            pauseLabel.Visible = isPaused;
            if (isPaused)
            {
                gameTimer.Stop();
            }
            else
            {
                gameTimer.Start();
                isAIPredict = false; // 取消AI预测显示
            }
            gamePanel.Invalidate();
        }

        private void ToggleAIPredict()
        {
            // 只使用本地算法进行预测，彻底下线DeepSeek交互
            isAIPredict = !isAIPredict;
            if (isAIPredict)
            {
                CalculateAIPrediction();
                gamePanel.Invalidate();
            }
            else
            {
                gamePanel.Invalidate();
            }
        }

        private bool IsValidPosition()
        {
            foreach (Point p in currentTetromino.Blocks)
            {
                int x = currentTetromino.Position.X + p.X;
                int y = currentTetromino.Position.Y + p.Y;

                // 检查是否超出边界
                if (x < 0 || x >= BoardWidth || y < 0 || y >= BoardHeight)
                {
                    return false;
                }

                // 检查是否与已有方块重叠
                if (y >= 0 && gameBoard[y, x] != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private void LockTetromino()
        {
            foreach (Point p in currentTetromino.Blocks)
            {
                int x = currentTetromino.Position.X + p.X;
                int y = currentTetromino.Position.Y + p.Y;

                if (y >= 0 && y < BoardHeight && x >= 0 && x < BoardWidth)
                {
                    gameBoard[y, x] = currentTetromino.ColorValue;
                }
            }
        }

        private void ClearLines()
        {
            int linesCleared = 0;

            for (int i = BoardHeight - 1; i >= 0; i--)
            {
                bool isLineFull = true;
                for (int j = 0; j < BoardWidth; j++)
                {
                    if (gameBoard[i, j] == 0)
                    {
                        isLineFull = false;
                        break;
                    }
                }

                if (isLineFull)
                {
                    linesCleared++;
                    // 将上面的行下移
                    for (int k = i; k > 0; k--)
                    {
                        for (int j = 0; j < BoardWidth; j++)
                        {
                            gameBoard[k, j] = gameBoard[k - 1, j];
                        }
                    }
                    // 清空最上面的行
                    for (int j = 0; j < BoardWidth; j++)
                    {
                        gameBoard[0, j] = 0;
                    }
                    i++; // 再次检查当前行，因为上面的行已经下移
                }
            }

            // 更新分数
            if (linesCleared > 0)
            {
                score += linesCleared;
                UpdateScoreLabel();
            }
        }

        private void SpawnNewTetromino()
        {
            currentTetromino = nextTetromino;
            nextTetromino = CreateRandomTetromino();
            
            // 增加方块计数
            blockCount++;

            // 检查游戏是否结束
            if (!IsValidPosition())
            {
                GameOver();
            }

            // 刷新下一个方块面板
            nextBlockPanel.Invalidate();
            
            // 重置帧时间
            lastFrameTime = DateTime.Now;
        }

        private void GameOver()
        {
            gameTimer.Stop();
            MessageBox.Show($"游戏结束！你的得分是：{score}", "游戏结束", MessageBoxButtons.OK, MessageBoxIcon.Information);
            InitializeGame();
        }

        private void UpdateScoreLabel()
        {
            scoreLabel.Text = $"分数: {score}";
        }

        private Tetromino CreateRandomTetromino()
        {
            Random random = new Random();
            int type = random.Next(7); // 7种不同的方块
            int colorValue = random.Next(1, 8); // 1-7的颜色值

            Tetromino tetromino = new Tetromino
            {
                Position = new Point(BoardWidth / 2 - 1, 0),
                ColorValue = colorValue,
                Color = GetColorFromValue(colorValue)
            };

            // 根据类型设置方块形状
            switch (type)
            {
                case 0: // I形方块
                    tetromino.Blocks = new Point[] { new Point(0, 0), new Point(0, 1), new Point(0, 2), new Point(0, 3) };
                    break;
                case 1: // J形方块
                    tetromino.Blocks = new Point[] { new Point(0, 0), new Point(0, 1), new Point(0, 2), new Point(-1, 2) };
                    break;
                case 2: // L形方块
                    tetromino.Blocks = new Point[] { new Point(0, 0), new Point(0, 1), new Point(0, 2), new Point(1, 2) };
                    break;
                case 3: // O形方块
                    tetromino.Blocks = new Point[] { new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(1, 1) };
                    break;
                case 4: // S形方块
                    tetromino.Blocks = new Point[] { new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(-1, 1) };
                    break;
                case 5: // T形方块
                    tetromino.Blocks = new Point[] { new Point(0, 0), new Point(-1, 1), new Point(0, 1), new Point(1, 1) };
                    break;
                case 6: // Z形方块
                    tetromino.Blocks = new Point[] { new Point(0, 0), new Point(-1, 0), new Point(0, 1), new Point(1, 1) };
                    break;
            }

            return tetromino;
        }

        private Color GetColorFromValue(int value)
        {
            switch (value)
            {
                case 1: return Color.Cyan;
                case 2: return Color.Blue;
                case 3: return Color.Orange;
                case 4: return Color.Yellow;
                case 5: return Color.Green;
                case 6: return Color.Purple;
                case 7: return Color.Red;
                default: return Color.Gray;
            }
        }

        private void CalculateAIPrediction()
        {
            // 清空之前的预测
            aiPredictedPositions.Clear();

            // 创建当前方块的副本用于模拟
            Tetromino simulationTetromino = new Tetromino
            {
                Blocks = (Point[])currentTetromino.Blocks.Clone(),
                Position = new Point(currentTetromino.Position.X, currentTetromino.Position.Y),
                Color = currentTetromino.Color,
                ColorValue = currentTetromino.ColorValue
            };

            // 简单AI：尝试找到能放置方块的最低位置
            int bestScore = -1;
            int bestX = 0;
            int bestRotation = 0;

            // 尝试所有可能的旋转
            for (int rotation = 0; rotation < 4; rotation++)
            {
                // 尝试所有可能的水平位置
                for (int x = -5; x < BoardWidth + 5; x++)
                {
                    // 重置模拟方块
                    simulationTetromino.Blocks = (Point[])currentTetromino.Blocks.Clone();
                    simulationTetromino.Position = new Point(currentTetromino.Position.X, currentTetromino.Position.Y);

                    // 应用旋转
                    for (int r = 0; r < rotation; r++)
                    {
                        RotateBlocks(simulationTetromino.Blocks);
                    }

                    // 移动到指定的水平位置
                    simulationTetromino.Position = new Point(x, 0);

                    // 如果位置无效，跳过
                    if (!IsValidPosition(simulationTetromino))
                    {
                        continue;
                    }

                    // 下落到最低点
                    while (IsValidPosition(simulationTetromino))
                    {
                        simulationTetromino.Position = new Point(simulationTetromino.Position.X, simulationTetromino.Position.Y + 1);
                    }
                    simulationTetromino.Position = new Point(simulationTetromino.Position.X, simulationTetromino.Position.Y - 1);

                    // 计算这个位置的分数
                    int score = EvaluatePosition(simulationTetromino);

                    // 更新最佳位置
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestRotation = rotation;
                    }
                }
            }

            // 应用最佳旋转
            simulationTetromino.Blocks = (Point[])currentTetromino.Blocks.Clone();
            for (int r = 0; r < bestRotation; r++)
            {
                RotateBlocks(simulationTetromino.Blocks);
            }

            // 移动到最佳水平位置
            simulationTetromino.Position = new Point(bestX, 0);

            // 下落到最低点
            while (IsValidPosition(simulationTetromino))
            {
                simulationTetromino.Position = new Point(simulationTetromino.Position.X, simulationTetromino.Position.Y + 1);
            }
            simulationTetromino.Position = new Point(simulationTetromino.Position.X, simulationTetromino.Position.Y - 1);

            // 记录预测位置
            foreach (Point p in simulationTetromino.Blocks)
            {
                int x = simulationTetromino.Position.X + p.X;
                int y = simulationTetromino.Position.Y + p.Y;
                if (x >= 0 && x < BoardWidth && y >= 0 && y < BoardHeight)
                {
                    aiPredictedPositions.Add(new Point(x, y));
                }
            }
        }
        
        private async Task CalculateAIPredictionWithDeepSeek()
        {
            try
            {
                // 清空之前的预测
                aiPredictedPositions.Clear();
                
                // 准备游戏状态描述
                string gameState = "";
                for (int y = 0; y < BoardHeight; y++)
                {
                    for (int x = 0; x < BoardWidth; x++)
                    {
                        gameState += gameBoard[y, x] + " ";
                    }
                    gameState += "\n";
                }
                
                // 准备当前方块描述
                string currentTetrominoState = "";
                foreach (Point p in currentTetromino.Blocks)
                {
                    currentTetrominoState += $"({p.X}, {p.Y}) ";
                }
                
                // 构建提示信息
                string prompt = $"你是俄罗斯方块AI。我会给你当前游戏板状态和当前方块。请计算最佳落点。\n\n游戏板状态 (0表示空，非0表示已占用):\n{gameState}\n当前方块形状: {currentTetrominoState}\n当前方块位置: X={currentTetromino.Position.X}, Y={currentTetromino.Position.Y}\n\n请以JSON格式返回最佳落点，包含x坐标和旋转次数。例如: {{\"x\": 3, \"rotation\": 2}}";
                
                // 构建DeepSeek API请求
                var request = new DeepSeekRequest
                {
                    messages = new List<DeepSeekMessage>
                    {
                        new DeepSeekMessage { role = "user", content = prompt }
                    }
                };
                
                // 发送请求到DeepSeek API
                var serializer = new JavaScriptSerializer();
                var content = new StringContent(serializer.Serialize(request), Encoding.UTF8, "application/json");
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", deepseekApiKey);
                
                var response = await httpClient.PostAsync("https://api.deepseek.com/chat/completions", content);
                response.EnsureSuccessStatusCode();
                
                var responseContent = await response.Content.ReadAsStringAsync();
                var deepseekResponse = serializer.Deserialize<DeepSeekResponse>(responseContent);
                
                if (deepseekResponse?.choices?.Count > 0)
                {
                    var aiResponse = deepseekResponse.choices[0].message.content;
                    
                    try
                    {
                        // 尝试解析JSON响应
                        int startIndex = aiResponse.IndexOf('{');
                        int endIndex = aiResponse.LastIndexOf('}');
                        
                        if (startIndex >= 0 && endIndex > startIndex)
                        {
                            string jsonPart = aiResponse.Substring(startIndex, endIndex - startIndex + 1);
                            var jsonSerializer = new JavaScriptSerializer();
                            var aiMove = jsonSerializer.Deserialize<Dictionary<string, object>>(jsonPart);
                            
                            if (aiMove.ContainsKey("x") && aiMove.ContainsKey("rotation"))
                            {
                                int bestX = Convert.ToInt32(aiMove["x"]);
                                int bestRotation = Convert.ToInt32(aiMove["rotation"]);
                                
                                // 创建当前方块的副本
                                Tetromino simulationTetromino = new Tetromino
                                {
                                    Blocks = (Point[])currentTetromino.Blocks.Clone(),
                                    Position = new Point(currentTetromino.Position.X, currentTetromino.Position.Y),
                                    Color = currentTetromino.Color,
                                    ColorValue = currentTetromino.ColorValue
                                };
                                
                                // 应用AI预测的旋转
                                for (int r = 0; r < bestRotation % 4; r++)
                                {
                                    RotateBlocks(simulationTetromino.Blocks);
                                }
                                
                                // 移动到AI预测的水平位置
                                simulationTetromino.Position = new Point(bestX, 0);
                                
                                // 如果位置有效，下落到最低点
                                if (IsValidPosition(simulationTetromino))
                                {
                                    while (IsValidPosition(simulationTetromino))
                                    {
                                        simulationTetromino.Position = new Point(simulationTetromino.Position.X, simulationTetromino.Position.Y + 1);
                                    }
                                    simulationTetromino.Position = new Point(simulationTetromino.Position.X, simulationTetromino.Position.Y - 1);
                                    
                                    // 记录预测位置
                                    foreach (Point p in simulationTetromino.Blocks)
                                    {
                                        int x = simulationTetromino.Position.X + p.X;
                                        int y = simulationTetromino.Position.Y + p.Y;
                                        if (x >= 0 && x < BoardWidth && y >= 0 && y < BoardHeight)
                                        {
                                            aiPredictedPositions.Add(new Point(x, y));
                                        }
                                    }
                                }
                                else
                                {
                                    // 如果AI预测的位置无效，回退到本地算法
                                    CalculateAIPrediction();
                                }
                            }
                            else
                            {
                                // 如果JSON解析失败，回退到本地算法
                                CalculateAIPrediction();
                            }
                        }
                        else
                        {
                            // 如果找不到JSON，回退到本地算法
                            CalculateAIPrediction();
                        }
                    }
                    catch
                    {
                        // 如果解析出错，回退到本地算法
                        CalculateAIPrediction();
                    }
                }
                else
                {
                    // 如果API响应无效，回退到本地算法
                    CalculateAIPrediction();
                }
            }
            catch
            {
                // 如果API调用出错，回退到本地算法
                CalculateAIPrediction();
            }
        }

        private bool IsValidPosition(Tetromino tetromino)
        {
            foreach (Point p in tetromino.Blocks)
            {
                int x = tetromino.Position.X + p.X;
                int y = tetromino.Position.Y + p.Y;

                // 检查是否超出边界
                if (x < 0 || x >= BoardWidth || y < 0 || y >= BoardHeight)
                {
                    return false;
                }

                // 检查是否与已有方块重叠
                if (y >= 0 && gameBoard[y, x] != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private void RotateBlocks(Point[] blocks)
        {
            // 顺时针旋转90度
            for (int i = 0; i < blocks.Length; i++)
            {
                int x = blocks[i].X;
                int y = blocks[i].Y;
                blocks[i] = new Point(y, -x);
            }
        }

        private int EvaluatePosition(Tetromino tetromino)
        {
            // 创建游戏板的副本
            int[,] simulationBoard = (int[,])gameBoard.Clone();

            // 将方块放置在模拟板上
            foreach (Point p in tetromino.Blocks)
            {
                int x = tetromino.Position.X + p.X;
                int y = tetromino.Position.Y + p.Y;
                if (y >= 0 && y < BoardHeight && x >= 0 && x < BoardWidth)
                {
                    simulationBoard[y, x] = tetromino.ColorValue;
                }
            }

            // 计算消除的行数
            int linesCleared = 0;
            for (int i = 0; i < BoardHeight; i++)
            {
                bool isLineFull = true;
                for (int j = 0; j < BoardWidth; j++)
                {
                    if (simulationBoard[i, j] == 0)
                    {
                        isLineFull = false;
                        break;
                    }
                }
                if (isLineFull)
                {
                    linesCleared++;
                }
            }

            // 计算高度和空洞
            int totalHeight = 0;
            int holes = 0;
            for (int j = 0; j < BoardWidth; j++)
            {
                int columnHeight = 0;
                bool foundBlock = false;
                for (int i = 0; i < BoardHeight; i++)
                {
                    if (simulationBoard[i, j] != 0)
                    {
                        foundBlock = true;
                        columnHeight = BoardHeight - i;
                        break;
                    }
                }

                if (foundBlock)
                {
                    totalHeight += columnHeight;

                    // 计算这一列中的空洞
                    for (int i = BoardHeight - columnHeight; i < BoardHeight; i++)
                    {
                        if (simulationBoard[i, j] == 0)
                        {
                            holes++;
                        }
                    }
                }
            }

            // 评分公式：消除行数的权重最大，其次是减少高度和空洞
            return linesCleared * 100 - totalHeight * 2 - holes * 5;
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TetrisGame());
        }
    }

    public class Tetromino
    {
        public Point[] Blocks { get; set; } // 方块的形状（相对坐标）
        public Point Position { get; set; } // 方块在游戏板上的位置
        public Color Color { get; set; } // 方块的颜色
        public int ColorValue { get; set; } // 方块的颜色值

        public void Rotate()
        {
            // 顺时针旋转90度
            for (int i = 0; i < Blocks.Length; i++)
            {
                int x = Blocks[i].X;
                int y = Blocks[i].Y;
                Blocks[i] = new Point(y, -x);
            }
        }
    }
}