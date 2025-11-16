using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
namespace Ataxx
{
    public partial class Form1 : Form
    {
        #region PVE
        struct BotMove
        {
            public int FromX;
            public int FromY;
            public int ToX;
            public int ToY;
            public bool IsValid => FromX >= 0 && FromY >= 0 && ToX >= 0 && ToY >= 0;
            public static BotMove Invalid => new BotMove { FromX = -1, FromY = -1, ToX = -1, ToY = -1 };
        }

        static readonly int INF = 1000000000;//const，暴力搜用到的上限次数
        const int ComputerMaxDepth = 5;
        const int HintMaxDepth = 4;
        const int ComputerSearchTimeMs = 2000;
        const int HintSearchTimeMs = 800;

        int currBotColor = -1;//属于-1/1表示法，默认玩家执白棋
        Point? hintFromCell;
        Point? hintToCell;
        static int[,] delta = new int[24, 2]{ { 1,1 },{ 0,1 },{ -1,1 },{ -1,0 },
        { -1,-1 },{ 0,-1 },{ 1,-1 },{ 1,0 },
        { 2,0 },{ 2,1 },{ 2,2 },{ 1,2 },
        { 0,2 },{ -1,2 },{ -2,2 },{ -2,1 },
        { -2,0 },{ -2,-1 },{ -2,-2 },{ -1,-2 },
        { 0,-2 },{ 1,-2 },{ 2,-2 },{ 2,-1 } };//一个棋子周围两圈X，Y坐标的增量，周围最多24个棋子，按顺序写的，可以用笔画一画，逆时针转的顺序
        bool inMap(int x, int y)// 判断是否在地图内
        {
            if (x < 0 || x > 6 || y < 0 || y > 6)
                return false;
            return true;
        }//pve
        int[,] BuildSearchBoard()
        {
            int[,] board = new int[7, 7];
            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    if (GridInfo[i, j] == 1)
                    {
                        board[i, j] = -1;
                    }
                    else if (GridInfo[i, j] == 2)
                    {
                        board[i, j] = 1;
                    }
                    else
                    {
                        board[i, j] = 0;
                    }
                }
            }
            return board;
        }
        int[,] CloneBoard(int[,] board)
        {
            int[,] copy = new int[7, 7];
            Array.Copy(board, copy, board.Length);
            return copy;
        }
        void ApplyVirtualMove(int[,] board, BotMove move, int color)
        {
            board[move.ToX, move.ToY] = color;
            if (Math.Abs(move.ToX - move.FromX) > 1 || Math.Abs(move.ToY - move.FromY) > 1)
            {
                board[move.FromX, move.FromY] = 0;
            }
            for (int dir = 0; dir < 8; dir++)
            {
                int nx = move.ToX + delta[dir, 0];
                int ny = move.ToY + delta[dir, 1];
                if (!inMap(nx, ny))
                    continue;
                if (board[nx, ny] == -color)
                {
                    board[nx, ny] = color;
                }
            }
        }
        int CountMoves(int[,] board, int color)
        {
            int count = 0;
            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    if (board[i, j] != color)
                        continue;
                    for (int dir = 0; dir < 24; dir++)
                    {
                        int nx = i + delta[dir, 0];
                        int ny = j + delta[dir, 1];
                        if (!inMap(nx, ny))
                            continue;
                        if (board[nx, ny] == 0)
                            count++;
                    }
                }
            }
            return count;
        }
        int ScoreMoveForOrdering(int[,] board, BotMove move, int color)
        {
            int score = 0;
            for (int dir = 0; dir < 8; dir++)
            {
                int nx = move.ToX + delta[dir, 0];
                int ny = move.ToY + delta[dir, 1];
                if (!inMap(nx, ny))
                    continue;
                if (board[nx, ny] == -color)
                    score += 3;
            }
            int distance = Math.Abs(move.ToX - move.FromX) + Math.Abs(move.ToY - move.FromY);
            int centerBias = 6 - (Math.Abs(move.ToX - 3) + Math.Abs(move.ToY - 3));
            score += centerBias;
            score -= distance;
            return score;
        }
        List<BotMove> GenerateMoves(int[,] board, int color)
        {
            List<BotMove> moves = new List<BotMove>();
            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    if (board[i, j] != color)
                        continue;
                    for (int dir = 0; dir < 24; dir++)
                    {
                        int nx = i + delta[dir, 0];
                        int ny = j + delta[dir, 1];
                        if (!inMap(nx, ny) || board[nx, ny] != 0)
                            continue;
                        moves.Add(new BotMove { FromX = i, FromY = j, ToX = nx, ToY = ny });
                    }
                }
            }
            moves.Sort((a, b) => ScoreMoveForOrdering(board, b, color).CompareTo(ScoreMoveForOrdering(board, a, color)));
            return moves;
        }
        int EvaluateBoard(int[,] board, int perspective)
        {
            int pieceScore = 0;
            int centerScore = 0;
            int frontierScore = 0;
            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    int cell = board[i, j];
                    if (cell == 0)
                        continue;
                    pieceScore += cell;
                    int centerDist = Math.Abs(3 - i) + Math.Abs(3 - j);
                    centerScore += (6 - centerDist) * cell;
                    bool frontier = false;
                    for (int dir = 0; dir < 8; dir++)
                    {
                        int nx = i + delta[dir, 0];
                        int ny = j + delta[dir, 1];
                        if (inMap(nx, ny) && board[nx, ny] == 0)
                        {
                            frontier = true;
                            break;
                        }
                    }
                    if (frontier)
                        frontierScore -= cell;
                    else
                        frontierScore += cell;
                }
            }
            int mobilityScore = CountMoves(board, perspective) - CountMoves(board, -perspective);
            int score = pieceScore * 100 + centerScore * 5 + frontierScore * 3;
            score *= perspective;
            score += mobilityScore * 8;
            return score;
        }
        (int score, BotMove move) AlphaBeta(int[,] board, int depth, int alpha, int beta, int color, Stopwatch timer, int timeLimitMs, ref bool timeUp)
        {
            if (timeUp || timer.ElapsedMilliseconds >= timeLimitMs)
            {
                timeUp = true;
                return (EvaluateBoard(board, color), BotMove.Invalid);
            }
            if (depth == 0)
            {
                return (EvaluateBoard(board, color), BotMove.Invalid);
            }
            List<BotMove> moves = GenerateMoves(board, color);
            if (moves.Count == 0)
            {
                if (CountMoves(board, -color) == 0)
                {
                    return (EvaluateBoard(board, color), BotMove.Invalid);
                }
                var passResult = AlphaBeta(board, depth - 1, -beta, -alpha, -color, timer, timeLimitMs, ref timeUp);
                return (-passResult.score, BotMove.Invalid);
            }
            int bestScore = -INF;
            BotMove bestMove = BotMove.Invalid;
            foreach (var move in moves)
            {
                int[,] nextBoard = CloneBoard(board);
                ApplyVirtualMove(nextBoard, move, color);
                var childResult = AlphaBeta(nextBoard, depth - 1, -beta, -alpha, -color, timer, timeLimitMs, ref timeUp);
                int score = -childResult.score;
                if (timeUp)
                {
                    return (score, bestMove);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }
                if (bestScore > alpha)
                {
                    alpha = bestScore;
                }
                if (alpha >= beta)
                {
                    break;
                }
            }
            return (bestScore, bestMove);
        }
        BotMove FindBestMove(int[,] board, int playerColor, int maxDepth, int timeLimitMs)
        {
            if (CountMoves(board, playerColor) == 0)
                return BotMove.Invalid;
            Stopwatch timer = Stopwatch.StartNew();
            BotMove bestMove = BotMove.Invalid;
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                bool timeUp = false;
                var result = AlphaBeta(board, depth, -INF, INF, playerColor, timer, timeLimitMs, ref timeUp);
                if (!timeUp && result.move.IsValid)
                {
                    bestMove = result.move;
                }
                if (timeUp || timer.ElapsedMilliseconds >= timeLimitMs)
                {
                    break;
                }
            }
            if (!bestMove.IsValid)
            {
                List<BotMove> fallbackMoves = GenerateMoves(board, playerColor);
                if (fallbackMoves.Count > 0)
                    bestMove = fallbackMoves[0];
            }
            return bestMove;
        }
        BotMove ComputeBestMoveForCurrentBoard(int playerColor, int maxDepth, int timeLimitMs)
        {
            int[,] board = BuildSearchBoard();
            return FindBestMove(board, playerColor, maxDepth, timeLimitMs);
        }
        void ClearHintOverlay()
        {
            if (!hintFromCell.HasValue && !hintToCell.HasValue)
                return;
            Graphics g = CreateGraphics();
            if (hintFromCell.HasValue)
            {
                draw.Draw(ref g, GridInfo, hintFromCell.Value.X, hintFromCell.Value.Y);
            }
            if (hintToCell.HasValue)
            {
                draw.Draw(ref g, GridInfo, hintToCell.Value.X, hintToCell.Value.Y);
            }
            g.Dispose();
            hintFromCell = hintToCell = null;
        }
        void ShowHintOnBoard(BotMove move)
        {
            ClearHintOverlay();
            if (!move.IsValid)
                return;
            using (Graphics g = CreateGraphics())
            {
                Rectangle startRect = new Rectangle(51 + move.FromX * squ, 51 + move.FromY * squ, squ - 2, squ - 2);
                Rectangle endRect = new Rectangle(51 + move.ToX * squ, 51 + move.ToY * squ, squ - 2, squ - 2);
                using (Pen startPen = new Pen(Color.DarkOrange, 3))
                {
                    g.DrawRectangle(startPen, startRect);
                }
                using (Pen endPen = new Pen(Color.MediumSeaGreen, 3))
                {
                    g.DrawRectangle(endPen, endRect);
                }
                if (GridInfo[move.ToX, move.ToY] == 0)
                {
                    using (Brush destBrush = new SolidBrush(Color.FromArgb(80, Color.MediumSeaGreen)))
                    {
                        int cnt = 0;
                        for (int dir = 0; dir < 24; dir++)//进行一步贪心
                        {
                            int x1 = i + delta[dir, 0];
                            int y1 = j + delta[dir, 1];
                            if (!inMap(x1, y1))
                                continue;
                            if (ConvertGridInfo[x1, y1] != 0)
                                continue;
                            cnt++;
                            beginPos[cnt, 0] = i;
                            beginPos[cnt, 1] = j;
                            possiblePos[cnt, 0] = x1;
                            possiblePos[cnt, 1] = y1;
                            ProcStep(beginPos[cnt, 0], beginPos[cnt, 1], possiblePos[cnt, 0], possiblePos[cnt, 1], color);
                            firstStep[cnt] = Valuation2(color);
                            for (int ii = 0; ii < 7; ii++)
                                for (int jj = 0; jj < 7; jj++)
                                    ConvertGridInfo[ii, jj] = tempgridInfo[0, ii, jj];//////tempgridInfo没有变化
                        }
                        for (int k = 1; k <= cnt; k++)
                            for (int l = 1; l <= cnt - k; l++)
                                if (firstStep[l] < firstStep[l + 1])
                                {
                                    Function function = new Function();
                                    function.swap(ref firstStep[l], ref firstStep[l + 1]);
                                    function.swap(ref possiblePos[l, 0], ref possiblePos[l + 1, 0]);
                                    function.swap(ref possiblePos[l, 1], ref possiblePos[l + 1, 1]);
                                    function.swap(ref beginPos[l, 0], ref beginPos[l + 1, 0]);
                                    function.swap(ref beginPos[l, 1], ref beginPos[l + 1, 1]);
                                }
                        for (int d = 1; d <= cnt; d++)
                        {
                            ProcStep(beginPos[d, 0], beginPos[d, 1], possiblePos[d, 0], possiblePos[d, 1], color);
                            alpha[1] = -INF;
                            beta[1] = INF;
                            Max_Min_Search(1, -1, color);////？
                            for (int ii = 0; ii < 7; ii++)
                                for (int jj = 0; jj < 7; jj++)
                                    ConvertGridInfo[ii, jj] = tempgridInfo[0, ii, jj];
                            if (alpha[0] > minn)
                            {
                                resultX = possiblePos[d, 0]; resultY = possiblePos[d, 1]; startX = i; startY = j;
                                minn = alpha[0];
                            }
                        }
                    }
                }
            }
            hintFromCell = new Point(move.FromX, move.FromY);
            hintToCell = new Point(move.ToX, move.ToY);
        }
        void GetHint(int C)  //为当前行棋的玩家（颜色为cc）提示一个电脑算出的较优解
        {
            if (Reset)
            {
                MessageBox.Show("请先开始游戏！", "WARNING!");
                return;
            }
            if (Exchange(C) == 0)
            {
                textBox1.Text = "当前无可走的棋步";
                ClearHintOverlay();
                return;
            }
            int searchColor = (C == 1) ? -1 : 1;
            BotMove hintMove = ComputeBestMoveForCurrentBoard(searchColor, HintMaxDepth, HintSearchTimeMs);
            if (!hintMove.IsValid)
            {
                textBox1.Text = "提示：暂未找到更优走法";
                ClearHintOverlay();
                return;
            }
            ShowHintOnBoard(hintMove);
            textBox1.Text = "提示: 将(" + (hintMove.FromX + 1).ToString() + "," + (hintMove.FromY + 1).ToString() + ")移动到(" +
                (hintMove.ToX + 1).ToString() + "," + (hintMove.ToY + 1).ToString() + ")";
        }//pve
        void RecordHistorySnapshot()
        {
            for (int i = 0; i < 7; i++)
                for (int j = 0; j < 7; j++)
                    history[StepSum, i, j] = GridInfo[i, j];
        }
        void AssimilateNeighbors(int centerX, int centerY, int playerColor, ref Graphics g)
        {
            int opponentColor = (playerColor == 1) ? 2 : 1;
            for (int dir = 0; dir < 8; dir++)
            {
                int nx = centerX + delta[dir, 0];
                int ny = centerY + delta[dir, 1];
                if (!inMap(nx, ny))
                    continue;
                if (GridInfo[nx, ny] == opponentColor)
                {
                    GridInfo[nx, ny] = playerColor;
                    draw.Draw(ref g, GridInfo, nx, ny);
                }
            }
        }
        void ApplyMoveOnBoard(int fromX, int fromY, int toX, int toY, int playerColor)
        {
            ClearHintOverlay();
            Graphics g = CreateGraphics();
            GridInfo[toX, toY] = playerColor;
            draw.Draw(ref g, GridInfo, toX, toY);
            if (Math.Abs(toX - fromX) > 1 || Math.Abs(toY - fromY) > 1)
            {
                GridInfo[fromX, fromY] = 0;
                draw.Draw(ref g, GridInfo, fromX, fromY);
            }
            AssimilateNeighbors(toX, toY, playerColor, ref g);
        }
        bool TryComputerMove()
        {
            int playerColor = Color;
            if (playerColor != 2)
                return false;
            currBotColor = (playerColor == 1) ? -1 : 1;
            BotMove bestMove = ComputeBestMoveForCurrentBoard(currBotColor, ComputerMaxDepth, ComputerSearchTimeMs);
            if (!bestMove.IsValid)
                return false;
            ApplyMoveOnBoard(bestMove.FromX, bestMove.FromY, bestMove.ToX, bestMove.ToY, playerColor);
            StepSum++;
            label9.Text = StepSum.ToString();
            CountPieces();
            WinJudgement();
            RecordHistorySnapshot();
            Color = (playerColor == 1) ? 2 : 1;
            label10.Text = (Color == 1) ? "○" : "●";
            textBox1.Text = "电脑已落子";
            return true;
        }
        void TriggerComputerTurn()
        {
            while (Color == 2)
            {
                if (Exchange(Color) == 0)
                {
                    textBox1.Text = "电脑无棋可走，玩家继续";
                    Color = 1;
                    label10.Text = "○";
                    if (Exchange(Color) == 0)
                    {
                        WinJudgement();
                    }
                    break;
                }
            resultX = possiblePos[0, 0]; resultY = possiblePos[0, 1]; startX = beginPos[0, 0]; startY = beginPos[0, 1];
            SearchBestChoice(cc);//搜索，把结果存在start&result中 //仍热用1/-1
            MessageBox.Show("提示:您可以将(" + (startX + 1).ToString() + "," + (startY + 1).ToString() + ")位置的棋子移动到(" + (resultX + 1).ToString() + "," + (resultY + 1).ToString() + ")处\n");
            //Draw(GridInfo, resultX + 1, resultY + 1);
        }//pve
        void RecordHistorySnapshot()
        {
            for (int i = 0; i < 7; i++)
                for (int j = 0; j < 7; j++)
                    history[StepSum, i, j] = GridInfo[i, j];
        }
        void AssimilateNeighbors(int centerX, int centerY, int playerColor, ref Graphics g)
        {
            int opponentColor = (playerColor == 1) ? 2 : 1;
            for (int dir = 0; dir < 8; dir++)
            {
                int nx = centerX + delta[dir, 0];
                int ny = centerY + delta[dir, 1];
                if (!inMap(nx, ny))
                    continue;
                if (GridInfo[nx, ny] == opponentColor)
                {
                    GridInfo[nx, ny] = playerColor;
                    draw.Draw(ref g, GridInfo, nx, ny);
                }
            }
        }
        void ApplyMoveOnBoard(int fromX, int fromY, int toX, int toY, int playerColor)
        {
            Graphics g = CreateGraphics();
            GridInfo[toX, toY] = playerColor;
            draw.Draw(ref g, GridInfo, toX, toY);
            if (Math.Abs(toX - fromX) > 1 || Math.Abs(toY - fromY) > 1)
            {
                GridInfo[fromX, fromY] = 0;
                draw.Draw(ref g, GridInfo, fromX, fromY);
            }
            AssimilateNeighbors(toX, toY, playerColor, ref g);
        }
        bool TryComputerMove()
        {
            int playerColor = Color;
            if (playerColor != 2)
                return false;
            ConvertGridData();
            currBotColor = (playerColor == 1) ? -1 : 1;
            startX = startY = resultX = resultY = -1;
            SearchBestChoice(currBotColor);
            if (!inMap(startX, startY) || !inMap(resultX, resultY))
                return false;
            if (GridInfo[startX, startY] != playerColor)
                return false;
            if (GridInfo[resultX, resultY] != 0)
                return false;
            if (Math.Abs(startX - resultX) > 2 || Math.Abs(startY - resultY) > 2)
                return false;
            ApplyMoveOnBoard(startX, startY, resultX, resultY, playerColor);
            StepSum++;
            label9.Text = StepSum.ToString();
            CountPieces();
            WinJudgement();
            RecordHistorySnapshot();
            Color = (playerColor == 1) ? 2 : 1;
            label10.Text = (Color == 1) ? "○" : "●";
            textBox1.Text = "电脑已落子";
            return true;
        }
        void TriggerComputerTurn()
        {
            while (Color == 2)
            {
                if (Exchange(Color) == 0)
                {
                    textBox1.Text = "电脑无棋可走，玩家继续";
                    Color = 1;
                    label10.Text = "○";
                    if (Exchange(Color) == 0)
                    {
                        WinJudgement();
                    }
                    break;
                }
                if (!TryComputerMove())
                {
                    Color = 1;
                    label10.Text = "○";
                    break;
                }
                if (Exchange(Color) == 0)
                {
                    if (Color == 1)
                    {
                        if (Exchange(2) == 0)
                        {
                            WinJudgement();
                            break;
                        }
                        textBox1.Text = "玩家无棋可走，电脑继续";
                        Color = 2;
                        label10.Text = "●";
                        continue;
                    }
                }
                break;
            }
        }
        public void Computer()
        {
            TriggerComputerTurn();
        }//电脑下棋
        #endregion

        #region PVP&主体
        int[,,] history = new int[200, 7, 7];//悔棋历史棋盘
        bool Reset = true;//是否重置游戏，打开程序默认初始重置
        bool JudgeGameStart = false;//判断对战（PVP或PVE）是否开始
        int[,] initGridInfo = new int[7, 7];//定义一个行棋最初始化的棋盘，四角各有一个棋子
        static int score1 = 0, score2 = 0;//1为白，白方得分

        //棋盘格子间距
        int squ = 81;//294 / 7=42

        int StepSum = 0;//操作步数
        int[,] GridInfo = new int[7, 7];//当前棋盘信息
        int xFirst,yFirst;//mouse down事件中记录的：鼠标按下选定要操作棋子位置的坐标
        int xSecond,ySecond;//mouse up事件中记录的：鼠标抬起将棋子拖到最终位置的坐标
        int Color = 1;//color，1为白，2为黑，默认白棋先行
        bool R;//true为PVP对战，false为PVE对战

        int xDown, yDown;//画辅助矩形可行棋盘范围坐标

        DrawGraph draw = new DrawGraph();
        Point p1 = new Point();
        Point p2 = new Point();
        Point p = new Point();

        public Form1()
        {
            InitializeComponent();
            CenterToScreen();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    initGridInfo[i, j] = 0;
                }
            }
            initGridInfo[0, 0] = 1;
            initGridInfo[0, 6] = 2;
            initGridInfo[6, 0] = 2;
            initGridInfo[6, 6] = 1;
        }//加载initgridinfo初始化棋盘
        public void init()
        {
            ClearHintOverlay();
            Graphics g = CreateGraphics();

            draw.DrawBoard(ref g);
            Color = 1;
            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    GridInfo[i, j] = 0;
                }
            }
            GridInfo[0, 0] = 1;
            GridInfo[0, 6] = 2;
            GridInfo[6, 0] = 2;
            GridInfo[6, 6] = 1;

            draw.Draw(ref g, GridInfo, 0, 0);
            draw.Draw(ref g, GridInfo, 0, 6);
            draw.Draw(ref g, GridInfo, 6, 0);
            draw.Draw(ref g, GridInfo, 6, 6);
            CountPieces(); label9.Text = StepSum.ToString();//算棋子数，并显示
        }//初始化当前棋盘，算棋子数，并显示
        public void DrawRec(ref Graphics g, int k)
        {
            int c; int d;
            int[,] pan = new int[5, 5];
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                {
                    pan[i, j] = 0;
                }
            p1 = Control.MousePosition;
            p = this.PointToClient(p1);
            xDown = (int)((p.X - 50) / squ);
            yDown = (int)((p.Y - 50) / squ);
            if (xDown == 0 && yDown == 0)//左上角绘方格
            {
                for (c = xDown; c < xDown + 3; c++)
                    for (d = yDown; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[0, 0]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 6 && yDown == 0)//右上角绘方格
            {
                for (c = xDown - 2; c < xDown + 1; c++)
                    for (d = yDown; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[1, 0]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 0 && yDown == 6)//左下角绘方格
            {
                for (c = xDown; c < xDown + 3; c++)
                    for (d = yDown - 2; d < yDown + 1; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[2, 0]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 6 && yDown == 6)//右下角绘方格
            {
                for (c = xDown - 2; c < xDown + 1; c++)
                    for (d = yDown - 2; d < yDown + 1; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[3, 0]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 1 && yDown == 0)//(1,0)
            {
                for (c = xDown - 1; c < xDown + 3; c++)
                    for (d = yDown; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[4, 0]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 0 && yDown == 1)//(0,1)
            {
                for (c = xDown; c < xDown + 3; c++)
                    for (d = yDown - 1; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[0, 1]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 0 && yDown == 5)//(0,5)
            {
                for (c = xDown; c < xDown + 3; c++)
                    for (d = yDown - 2; d < yDown + 2; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[1, 1]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 5 && yDown == 0)//(5,0)
            {
                for (c = xDown - 2; c < xDown + 2; c++)
                    for (d = yDown; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[2, 1]++; GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 1 && yDown == 6)//(1,6)
            {
                for (c = xDown - 1; c < xDown + 3; c++)
                    for (d = yDown - 2; d < yDown + 1; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[3, 1]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 5 && yDown == 6)//(5,6)
            {
                for (c = xDown - 2; c < xDown + 2; c++)
                    for (d = yDown - 2; d < yDown + 1; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[4, 1]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 6 && yDown == 5)//(6,5)
            {
                for (c = xDown - 2; c < xDown + 1; c++)
                    for (d = yDown - 2; d < yDown + 2; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[0, 2]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 6 && yDown == 1)//(6,1)
            {
                for (c = xDown - 2; c < xDown + 1; c++)
                    for (d = yDown - 1; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[1, 2]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 1 && yDown == 1)//(1,1)
            {
                for (c = xDown - 1; c < xDown + 3; c++)
                    for (d = yDown - 1; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[2, 2]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 5 && yDown == 5)//(5,5)
            {
                for (c = xDown - 2; c < xDown + 2; c++)
                    for (d = yDown - 2; d < yDown + 2; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[3, 2]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 1 && yDown == 5)//(1,5)
            {
                for (c = xDown - 1; c < xDown + 3; c++)
                    for (d = yDown - 2; d < yDown + 2; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[4, 2]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 5 && yDown == 1)//(5,1)
            {
                for (c = xDown - 2; c < xDown + 2; c++)
                    for (d = yDown - 1; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[0, 3]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 0 && yDown != 0 && yDown != 1 && yDown != 5 && yDown != 6)//(左0列)
            {
                for (c = xDown; c < xDown + 3; c++)
                    for (d = yDown - 2; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[1, 3]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 6 && yDown != 0 && yDown != 1 && yDown != 5 && yDown != 6)//(右6列)
            {
                for (c = xDown - 2; c < xDown + 1; c++)
                    for (d = yDown - 2; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[2, 3]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (yDown == 0 && xDown != 0 && xDown != 1 && xDown != 5 && xDown != 6)//(上0行)
            {
                for (c = xDown - 2; c < xDown + 3; c++)
                    for (d = yDown; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[3, 3]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (yDown == 6 && xDown != 0 && xDown != 1 && xDown != 5 && xDown != 6)//(下6行)
            {
                for (c = xDown - 2; c < xDown + 3; c++)
                    for (d = yDown - 2; d < yDown + 1; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[4, 3]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 1 && yDown != 0 && yDown != 1 && yDown != 5 && yDown != 6)//(左1列)
            {
                for (c = xDown - 1; c < xDown + 3; c++)
                    for (d = yDown - 2; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[0, 4]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (yDown == 1 && xDown != 0 && xDown != 1 && xDown != 5 && xDown != 6)//(上1行)
            {
                for (c = xDown - 2; c < xDown + 3; c++)
                    for (d = yDown - 1; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[1, 4]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (yDown == 5 && xDown != 0 && xDown != 1 && xDown != 5 && xDown != 6)//(下5行)
            {
                for (c = xDown - 2; c < xDown + 3; c++)
                    for (d = yDown - 2; d < yDown + 2; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[2, 4]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown == 5 && yDown != 0 && yDown != 1 && yDown != 5 && yDown != 6)//(右5列)
            {
                for (c = xDown - 2; c < xDown + 2; c++)
                    for (d = yDown - 2; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[3, 4]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
            if (xDown > 1 && xDown < 5 && yDown > 1 && yDown < 5)//(内部九个格)
            {
                for (c = xDown - 2; c < xDown + 3; c++)
                    for (d = yDown - 2; d < yDown + 3; d++)
                    {
                        if (GridInfo[c, d] == 0)
                        {
                            pan[4, 4]++;
                            GridInfo[c, d] = k;
                            draw.Draw(ref g, GridInfo, c, d);
                        }
                    }
            }
        }  //找出规定区域：画辅助矩形可行棋盘范围
        public void SetZero(int[,] arr)
        {
            Graphics g = CreateGraphics();
            for (int i = 0; i < 7; i++)
                for (int j = 0; j < 7; j++)
                {
                    if (arr[i, j] == 3)
                    {
                        arr[i, j] = 0;
                        draw.Draw(ref g, arr, i, j);
                    }
                    else if (arr[i, j] == 4)
                    {
                        arr[i, j] = 0;
                        draw.Draw(ref g, arr, i, j);
                    }
                }
        }//遍历功能，非1非2的都置为0：画辅助矩形时3，4作为颜色写入到当前棋盘信息（gridinfo），为避免影响画完就刷掉
        public void CountPieces()
        {
            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    if (GridInfo[i, j] == 1)
                    {
                        score1++;
                    }
                    else if (GridInfo[i, j] == 2)
                    {
                        score2++;
                    }
                }
            }
            label1.Text = Convert.ToString(score1);
            label2.Text = Convert.ToString(score2);
            score1 = score2 = 0;
        }//计算分数的方法，并显示
        public void PiecesSet(int a)
        {
            ClearHintOverlay();
            //Point p2 = new Point();
            p2 = Control.MousePosition;
            p = this.PointToClient(p2);
            if (50 <= p.X && p.X <= (50 + squ * 7) && p.Y <= 50 + squ * 7 && p.Y >= 50)
            {
                Graphics g = this.CreateGraphics();
                xSecond = (int)((p.X - 50) / squ);
                ySecond = (int)((p.Y - 50) / squ);
                if (GridInfo[xFirst, yFirst] == a && GridInfo[xSecond, ySecond] == 0)
                {
                    if ((xSecond == xFirst - 1 && ySecond == yFirst - 1) || (xSecond == xFirst && ySecond == yFirst - 1)//临近8格
                     || (xSecond == xFirst - 1 && ySecond == yFirst) || (xSecond == xFirst - 1 && ySecond == yFirst + 1)
                        || (xSecond == xFirst + 1 && ySecond == yFirst - 1) || (xSecond == xFirst + 1 && ySecond == yFirst)
                        || (xSecond == xFirst && ySecond == yFirst + 1) || (xSecond == xFirst + 1 && ySecond == yFirst + 1))
                    {
                        GridInfo[xSecond, ySecond] = a;
                        draw.Draw(ref g, GridInfo, xSecond, ySecond);

                    }
                    if ((xSecond == xFirst - 2 && ySecond == yFirst - 2) || (xSecond == xFirst - 2 && ySecond == yFirst - 1)
                     || (xSecond == xFirst - 2 && ySecond == yFirst) || (xSecond == xFirst - 2 && ySecond == yFirst + 1)
                     || (xSecond == xFirst - 2 && ySecond == yFirst + 2) || (xSecond == xFirst + 2 && ySecond == yFirst - 2)
                     || (xSecond == xFirst + 2 && ySecond == yFirst - 1) || (xSecond == xFirst + 2 && ySecond == yFirst)
                     || (xSecond == xFirst + 2 && ySecond == yFirst + 1) || (xSecond == xFirst + 2 && ySecond == yFirst + 2)
                     || (xSecond == xFirst - 1 && ySecond == yFirst + 2) || (xSecond == xFirst && ySecond == yFirst + 2)
                     || (xSecond == xFirst + 1 && ySecond == yFirst + 2) || (xSecond == xFirst - 1 && ySecond == yFirst - 2)
                     || (xSecond == xFirst && ySecond == yFirst - 2) || (xSecond == xFirst + 1 && ySecond == yFirst - 2))
                    {
                        GridInfo[xSecond, ySecond] = a;
                        draw.Draw(ref g, GridInfo, xSecond, ySecond);
                        GridInfo[xFirst, yFirst] = 0;
                        draw.Draw(ref g, GridInfo, xFirst, yFirst);
                    }
                }
            }
        }//复制和剪切的方法，拖拽棋子
        public void Process(int c, int d)
        {
            Graphics g = CreateGraphics();
            if (xSecond == 0 && ySecond == 0)//判断左上角棋子
            {
                if (GridInfo[xSecond + 1, ySecond] == c)
                { GridInfo[xSecond + 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond); }
                if (GridInfo[xSecond + 1, ySecond + 1] == c)
                { GridInfo[xSecond + 1, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond + 1); }
                if (GridInfo[xSecond, ySecond + 1] == c)
                { GridInfo[xSecond, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond + 1); }
            }
            if (xSecond == 0 && ySecond == 6)//判断左下角棋子
            {
                if (GridInfo[xSecond + 1, ySecond] == c)
                { GridInfo[xSecond + 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond); }
                if (GridInfo[xSecond + 1, ySecond - 1] == c)
                { GridInfo[xSecond + 1, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond - 1); }
                if (GridInfo[xSecond, ySecond - 1] == c)
                { GridInfo[xSecond, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond - 1); }
            }
            if (xSecond == 6 && ySecond == 0)//判断右上角棋子
            {
                if (GridInfo[xSecond - 1, ySecond] == c)
                { GridInfo[xSecond - 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond); }
                if (GridInfo[xSecond - 1, ySecond + 1] == c)
                { GridInfo[xSecond - 1, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond + 1); }
                if (GridInfo[xSecond, ySecond + 1] == c)
                { GridInfo[xSecond, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond + 1); }
            }
            if (xSecond == 6 && ySecond == 6)//判断右下角棋子
            {
                if (GridInfo[xSecond - 1, ySecond] == c)
                { GridInfo[xSecond - 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond); }
                if (GridInfo[xSecond - 1, ySecond - 1] == c)
                { GridInfo[xSecond - 1, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond - 1); }
                if (GridInfo[xSecond, ySecond - 1] == c)
                { GridInfo[xSecond, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond - 1); }
            }
            if (xSecond == 0 && ySecond != 0 && ySecond != 6)//判断最左列棋子
            {
                if (GridInfo[xSecond + 1, ySecond] == c) { GridInfo[xSecond + 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond); }
                if (GridInfo[xSecond + 1, ySecond + 1] == c) { GridInfo[xSecond + 1, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond + 1); }
                if (GridInfo[xSecond + 1, ySecond - 1] == c) { GridInfo[xSecond + 1, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond - 1); }
                if (GridInfo[xSecond, ySecond - 1] == c) { GridInfo[xSecond, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond - 1); }
                if (GridInfo[xSecond, ySecond + 1] == c) { GridInfo[xSecond, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond + 1); }
            }
            if (ySecond == 0 && xSecond != 0 && xSecond != 6)//判断最上行棋子
            {
                if (GridInfo[xSecond + 1, ySecond] == c) { GridInfo[xSecond + 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond); }
                if (GridInfo[xSecond + 1, ySecond + 1] == c) { GridInfo[xSecond + 1, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond + 1); }
                if (GridInfo[xSecond, ySecond + 1] == c) { GridInfo[xSecond, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond + 1); }
                if (GridInfo[xSecond - 1, ySecond] == c) { GridInfo[xSecond - 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond); }
                if (GridInfo[xSecond - 1, ySecond + 1] == c) { GridInfo[xSecond - 1, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond + 1); }
            }
            if (xSecond == 6 && ySecond != 0 && ySecond != 6)//判断最右列棋子
            {
                if (GridInfo[xSecond, ySecond - 1] == c) { GridInfo[xSecond, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond - 1); }
                if (GridInfo[xSecond, ySecond + 1] == c) { GridInfo[xSecond, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond + 1); }
                if (GridInfo[xSecond - 1, ySecond] == c) { GridInfo[xSecond - 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond); }
                if (GridInfo[xSecond - 1, ySecond - 1] == c) { GridInfo[xSecond - 1, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond - 1); }
                if (GridInfo[xSecond - 1, ySecond + 1] == c) { GridInfo[xSecond - 1, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond + 1); }
            }
            if (ySecond == 6 && xSecond != 0 && xSecond != 6)//判断最下行棋子
            {
                if (GridInfo[xSecond + 1, ySecond] == c) { GridInfo[xSecond + 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond); }
                if (GridInfo[xSecond + 1, ySecond - 1] == c) { GridInfo[xSecond + 1, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond - 1); }
                if (GridInfo[xSecond, ySecond - 1] == c) { GridInfo[xSecond, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond - 1); }
                if (GridInfo[xSecond - 1, ySecond] == c) { GridInfo[xSecond - 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond); }
                if (GridInfo[xSecond - 1, ySecond - 1] == c) { GridInfo[xSecond - 1, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond - 1); }
            }
            if (xSecond != 0 && xSecond != 6 && ySecond != 0 && ySecond != 6)//判断5*5内部棋子
            {
                if (GridInfo[xSecond + 1, ySecond] == c)
                { GridInfo[xSecond + 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond); }
                if (GridInfo[xSecond + 1, ySecond + 1] == c)
                { GridInfo[xSecond + 1, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond + 1); }
                if (GridInfo[xSecond + 1, ySecond - 1] == c)
                { GridInfo[xSecond + 1, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond + 1, ySecond - 1); }
                if (GridInfo[xSecond, ySecond - 1] == c)
                { GridInfo[xSecond, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond - 1); }
                if (GridInfo[xSecond, ySecond + 1] == c)
                { GridInfo[xSecond, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond, ySecond + 1); }
                if (GridInfo[xSecond - 1, ySecond] == c)
                { GridInfo[xSecond - 1, ySecond] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond); }
                if (GridInfo[xSecond - 1, ySecond - 1] == c)
                { GridInfo[xSecond - 1, ySecond - 1] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond - 1); }
                if (GridInfo[xSecond - 1, ySecond + 1] == c)
                { GridInfo[xSecond - 1, ySecond + 1] = d; draw.Draw(ref g, GridInfo, xSecond - 1, ySecond + 1); }
            }

        }//周围棋子同化
        public void WinJudgement()
        {
            int a = int.Parse(label1.Text);
            int b = int.Parse(label2.Text);
            if (a + b == 49)
            {
                if (a > b)
                {
                    MessageBox.Show("White Win!");
                }
                else if (a < b)
                {
                    MessageBox.Show("Black Win!");
                }
            }
            if (a == 0)
            {
                MessageBox.Show("Black Win!");
            }
            else if (b == 0)
            {
                MessageBox.Show("White Win!");
            }
        }//不完善的胜负判定，在下面Exchange函数补充了
        private int Exchange(int m)
        {
            int sum = 0;
            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    if (GridInfo[i, j] == 0)
                    {
                        {
                            if (i == 0 && j == 0)//左上角绘方格
                            {
                                for (int c = i; c < i + 3; c++)
                                {
                                    for (int d = j; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 6 && j == 0)//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 1; c++)
                                {
                                    for (int d = j; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 0 && j == 6)
                            {
                                for (int c = i; c < i + 3; c++)
                                {
                                    for (int d = j - 2; d < j + 1; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 6 && j == 6)//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 1; c++)
                                {
                                    for (int d = j - 2; d < j + 1; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }

                            if (i == 1 && j == 0)//左上角绘方格
                            {
                                for (int c = i - 1; c < i + 3; c++)
                                {
                                    for (int d = j; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 0 && j == 1)//左上角绘方格
                            {
                                for (int c = i; c < i + 3; c++)
                                {
                                    for (int d = j - 1; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 5 && j == 0)//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 2; c++)
                                {
                                    for (int d = j; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 0 && j == 5)//左上角绘方格
                            {
                                for (int c = i; c < i + 3; c++)
                                {
                                    for (int d = j - 2; d < j + 2; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 1 && j == 6)//左上角绘方格
                            {
                                for (int c = i - 1; c < i + 3; c++)
                                {
                                    for (int d = j - 2; d < j + 1; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 6 && j == 1)//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 1; c++)
                                {
                                    for (int d = j - 1; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 6 && j == 5)//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 1; c++)
                                {
                                    for (int d = j - 2; d < j + 2; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 5 && j == 6)//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 2; c++)
                                {
                                    for (int d = j - 2; d < j + 1; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }

                            if (i == 0 && (j == 3 || j == 4 || j == 2))//左上角绘方格
                            {
                                for (int c = i; c < i + 3; c++)
                                {
                                    for (int d = j - 2; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 6 && (j == 3 || j == 4 || j == 2))//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 1; c++)
                                {
                                    for (int d = j - 2; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (j == 0 && (i == 3 || i == 4 || i == 2))//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 3; c++)
                                {
                                    for (int d = j; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (j == 6 && (i == 3 || i == 4 || i == 2))//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 3; c++)
                                {
                                    for (int d = j - 2; d < j + 1; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }

                            if (i == 1 && j == 1)//左上角绘方格
                            {
                                for (int c = i - 1; c < i + 3; c++)
                                {
                                    for (int d = j - 1; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 1 && j == 5)//左上角绘方格
                            {
                                for (int c = i - 1; c < i + 3; c++)
                                {
                                    for (int d = j - 2; d < j + 2; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 5 && j == 5)//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 2; c++)
                                {
                                    for (int d = j - 2; d < j + 2; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 5 && j == 1)//左上角绘方格
                            {
                                for (int c = i - 2; c < i + 2; c++)
                                {
                                    for (int d = j - 1; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }

                            if (i == 1 && (j == 3 || j == 4 || j == 2))//左上角绘方格
                            {
                                for (int c = i - 1; c < i + 3; c++)
                                {
                                    for (int d = j - 2; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (i == 5 && (j == 3 || j == 4 || j == 2))
                            {
                                for (int c = i - 2; c < i + 2; c++)
                                {
                                    for (int d = j - 2; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (j == 5 && (i == 3 || i == 4 || i == 2))
                            {
                                for (int c = i - 2; c < i + 3; c++)
                                {
                                    for (int d = j - 2; d < j + 2; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if (j == 1 && (i == 3 || i == 4 || i == 2))
                            {
                                for (int c = i - 2; c < i + 3; c++)
                                {
                                    for (int d = j - 1; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                            if ((i == 2 || i == 3 || i == 4) && (j == 2 || j == 3 || j == 4))
                            {
                                for (int c = i - 2; c < i + 3; c++)
                                {
                                    for (int d = j - 2; d < j + 3; d++)
                                    {
                                        if (GridInfo[c, d] == m)
                                        {
                                            sum += 1;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return sum;
        }//行棋特殊情况：一方无棋可走，换至另一方
        public string printHelp()
        {
            string str = "                                           同化棋(Ataxx),是一种双人对战棋类，\n                               Dave Crummack与Craig Galley在1988年发明,\n                                        并1990年出品于电视游戏而广为流行.\n";
            str += ("                                               游戏采用7*7方格棋盘,\n                                        并以黑(●)白(○)棋子区分敌我.\n         玩家可自由选择执黑(先手)或执白(后手)[本游戏中，默认玩家执白先行].\n");
            str += ("                            初始布置为双方各将两枚棋子放在最外的对角格.\n");
            str += ("  玩家必须轮流移动一枚己子到一个空棋位,该棋位可以是邻近八格(包括对角相邻的格)之一，或相隔一格的次邻八格之一.\n");
            str += ("                              移动会使新棋位邻近八格的所有敌棋变成己方.\n                *注意:如果棋子移到的是邻接八格,会有一颗新己棋出现在原先棋位.\n");
            str += ("                     无法行棋需弃权.当两方都无法行棋时,游戏结束.以最多子者胜.\n");
            return str;
        }//HELP
        public void LoadGame(string str)
        {
            Graphics g = CreateGraphics();
            var reader1 = new StreamReader(str);
            string t = reader1.ReadLine();
            var buf = t.Split(',');
            for (int i = 0; i < 7; i++)
                for (int j = 0; j < 7; j++)
                {
                    GridInfo[i, j] = Convert.ToInt32(buf[7 * i + j]);
                    history[0, i, j] = GridInfo[i, j];
                    draw.Draw(ref g, GridInfo, i, j);
                }
            int sup = Convert.ToInt32(buf[49]);//第50个来判断轮到谁走
            if (sup == 1)
            {
                Color = 2;
                label10.Text = "●";
            }
            else
            {
                Color = 1;
                label10.Text = "○";
            }
            reader1.Close();
        }//读入存档数据
        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            if (Reset == false)
            {
                textBox1.Clear();
                label9.Text = StepSum.ToString();
                Graphics g = CreateGraphics();
                //Point p1 = new Point();
                p1 = Control.MousePosition;
                p = this.PointToClient(p1);
                //Historylist.Add(GridInfo);
                if (50 <= p.X && p.X <= (50 + squ * 7) && p.Y <= 50 + squ * 7 && p.Y >= 50)
                {
                    xFirst = (int)((p.X - 50) / squ);
                    yFirst = (int)((p.Y - 50) / squ);
                    if (GridInfo[xFirst, yFirst] == 0)
                    {
                        textBox1.Text = ("请拾取棋子");
                    }

                    if (R == true)//pvp
                    {

                        if (Color == 1)
                        {
                            if (GridInfo[xFirst, yFirst] == 1)
                            {
                                DrawRec(ref g, 3);
                            }
                            else if (GridInfo[xFirst, yFirst] == 2)
                            {
                                textBox1.Text = ("请对手落子");
                            }
                        }
                        else if (Color == 2)
                        {
                            if (GridInfo[xFirst, yFirst] == 2)
                            {
                                DrawRec(ref g, 4);
                            }
                            else if (GridInfo[xFirst, yFirst] == 1)
                            {
                                textBox1.Text = ("请对手落子");
                            }
                        }
                    }
                    else if (R == false)//pve
                    {
                        if (GridInfo[xFirst, yFirst] == 1)
                        {
                            DrawRec(ref g, 3);
                        }
                        else if (GridInfo[xFirst, yFirst] == 2)
                        {
                            textBox1.Text = ("请玩家落子");
                        }
                    }
                    else
                    {
                        textBox1.Text = ("请点击棋盘区域");
                    }
                }
            }
        }//记录鼠标落下的棋子坐标//调动函数行棋的关键事件
        private void Form1_MouseUp(object sender, MouseEventArgs e)
        {
            if (Reset == false)
            {
                //Point p2 = new Point();
                p2 = Control.MousePosition;
                p = this.PointToClient(p2);

                if (50 <= p.X && p.X <= (50 + squ * 7) && p.Y <= 50 + squ * 7 && p.Y >= 50)
                {
                    Graphics g = this.CreateGraphics();
                    xSecond = (int)((p.X - 50) / squ);
                    ySecond = (int)((p.Y - 50) / squ);
                    if (GridInfo[xSecond, ySecond] != 1 && GridInfo[xSecond, ySecond] != 2)
                    {
                        if (R == true)
                        {
                            if ((xSecond - xFirst <= 2 && xSecond - xFirst >= -2) && (ySecond - yFirst <= 2 && ySecond - yFirst >= -2))
                            {
                                if (GridInfo[xFirst, yFirst] == 1 && Color == 1)
                                {
                                    SetZero(GridInfo);
                                    PiecesSet(1); Process(2, 1); CountPieces(); Color = 2; label10.Text = "●";
                                    StepSum++; label9.Text = StepSum.ToString(); WinJudgement();
                                    //bestchoice（） c=1
                                }
                                else if (GridInfo[xFirst, yFirst] == 2 && Color == 2)
                                {
                                    SetZero(GridInfo);
                                    PiecesSet(2); Process(1, 2); CountPieces(); Color = 1; label10.Text = "○";
                                    StepSum++; label9.Text = StepSum.ToString(); WinJudgement();
                                }
                                for (int i = 0; i < 7; i++)
                                {
                                    for (int j = 0; j < 7; j++)
                                    {
                                        history[StepSum, i, j] = GridInfo[i, j];
                                    }
                                }
                                Exchange(Color);
                                if (Exchange(Color) == 0)
                                {
                                    if (Color == 1)
                                    {
                                        Color = 2;
                                        label10.Text = "●";
                                    }
                                    else
                                    {
                                        Color = 1;
                                        label10.Text = "○";
                                    }
                                }
                            }
                            else
                            {
                                textBox1.Text = ("请在规定区域内落子");
                            }

                        }
                        else if (R == false)
                        {
                            if ((xSecond - xFirst <= 2 && xSecond - xFirst >= -2) && (ySecond - yFirst <= 2 && ySecond - yFirst >= -2))
                            {
                                if (GridInfo[xFirst, yFirst] == 1 && Color == 1)
                                {
                                    SetZero(GridInfo);
                                    PiecesSet(1);
                                    Process(2, 1);
                                    CountPieces();
                                    Color = 2;
                                    label10.Text = "●";
                                    StepSum++;
                                    label9.Text = StepSum.ToString();
                                    WinJudgement();
                                    RecordHistorySnapshot();
                                    if (Exchange(Color) == 0)
                                    {
                                        textBox1.Text = "电脑无棋可走，玩家继续";
                                        Color = 1;
                                        label10.Text = "○";
                                        if (Exchange(Color) == 0)
                                        {
                                            WinJudgement();
                                        }
                                    }
                                    else
                                    {
                                        Computer();
                                    }
                                }
                                else
                                {
                                    textBox1.Text = ("请拾取白棋");
                                }
                            }
                            else
                            {
                                textBox1.Text = ("请在规定区域内落子");
                            }
                        }
                        else
                        {
                            textBox1.Text = ("请在空白处落子");
                        }
                    }
                    else
                    {
                        textBox1.Text = ("请在棋盘上落子");
                    }
                }
            }

        }//记录鼠标弹起时的坐标,并走棋//调动函数行棋的关键事件
        private void button3_Click(object sender, EventArgs e)
        {
            label10.Text = "○";
            Reset = false;
            init();
            R = true;
            JudgeGameStart = true;
            label9.Text = "--";
            label1.Text = "--";
            label2.Text = "--";
            label10.Text = "--";
            textBox1.Clear();
            textBox1.Text = "白子先行";
            StepSum = 0;
            history[0, 0, 0] = 1;
            history[0, 6, 6] = 1;
            history[0, 6, 0] = 2;
            history[0, 0, 6] = 2;
        }//PVP对战开始
        private void button2_Click_1(object sender, EventArgs e)
        {
            //JudgeGameStart = true;//注释掉使读存档只能人人打
            R = false;
            Reset = false;
            label9.Text = "--";
            label1.Text = "--";
            label2.Text = "--";
            label10.Text = "--";
            textBox1.Clear();
            init();
            MessageBox.Show("Player goes white first!");
        }//PVE对战开始
        private void button4_Click(object sender, EventArgs e)//重置对战
        {
            Reset = true;
            JudgeGameStart = false;
            StepSum = 0;
            init();
            label9.Text = "--";
            label1.Text = "--";
            label2.Text = "--";
            label10.Text = "--";
            textBox1.Clear();
            for (int m = 0; m < 200; m++)
                for (int i = 0; i < 7; i++)
                    for (int j = 0; j < 7; j++)
                    {
                        history[m, i, j] = 0;
                    }
            history[0, 0, 0] = 1;
            history[0, 6, 6] = 1;
            history[0, 6, 0] = 2;
            history[0, 0, 6] = 2;
        }
        private void button8_Click(object sender, EventArgs e)
        {
            GetHint(Color);
        }//获得提示
        private void button6_Click(object sender, EventArgs e)
        {
            Graphics g = CreateGraphics();
            ClearHintOverlay();
            if (JudgeGameStart == true)
            {
                if (StepSum > 0)
                {
                    if (Color == 1)
                    { Color = 2; label10.Text = "●"; }
                    else if (Color == 2)
                    { Color = 1; label10.Text = "○"; }
                    for (int i = 0; i < 7; i++)
                    {
                        for (int j = 0; j < 7; j++)
                        {
                            GridInfo[i, j] = history[StepSum - 1, i, j];//历史棋盘
                            draw.Draw(ref g, GridInfo, i, j);
                        }
                    }
                    StepSum = StepSum - 1;
                    label9.Text = StepSum.ToString();
                    for (int i = 0; i < 7; i++)
                    {
                        for (int j = 0; j < 7; j++)
                        {
                            if (GridInfo[i, j] == 3)
                            {
                                GridInfo[i, j] = 0;
                            }
                            else if (GridInfo[i, j] == 4)
                            {
                                GridInfo[i, j] = 0;
                            }
                        }
                    }
                    CountPieces();
                }
            }
            else
            {
                MessageBox.Show("请先开始游戏！", "WARNING!");
            }
        }//悔棋
        private void button1_Click_1(object sender, EventArgs e)
        {
            MessageBox.Show(printHelp(), "<Help>");
        }//显示Help
        private void button9_Click(object sender, EventArgs e)
        {
            WEB Link = new WEB();
            Link.Web();
        }//网页超链接
        private void button7_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK && JudgeGameStart == true)
            {
                LoadGame(openFileDialog1.FileName);
            }
            else if (JudgeGameStart == false)
            {
                MessageBox.Show("请先开始游戏！", "WARNING!");
            }
            else
            {
                MessageBox.Show("Wrong!");
            }
        }//对话框读存档数据
        private void button5_Click(object sender, EventArgs e)
        {
            if (JudgeGameStart == true)
            {
                SaveFileDialog file = new SaveFileDialog();
                file.InitialDirectory = Application.StartupPath;
                file.RestoreDirectory = true;
                file.Filter = "All Files(*.*)|*.*|Dat Files(*.dat)|*.dat|Text Files(*.txt)|*.txt";
                file.FilterIndex = 3;
                if (file.ShowDialog() == DialogResult.OK)
                {
                    var stream = new FileStream(file.FileName, FileMode.Create);
                    var writer = new StreamWriter(stream);

                    string data = "";
                    for (int i = 0; i < 7; i++)
                        for (int j = 0; j < 7; j++)
                            data += GridInfo[i, j].ToString() + ",";

                    data += (label10.Text == "●") ? 1 : 2;

                    writer.Write(data);
                    writer.Close();
                    stream.Close();
                }
            }
            
        }//保存游戏存档，注意如果保存新存档，输入存档名最好加上‘.txt’（不加读文件也能读），也可以替换现有txt文件（存档）
        #endregion
    }
    internal class Function
    {
        internal void swap(ref int v1, ref int v2)
        {
            var temp = v2;
            v2 = v1;
            v1 = temp;
        }
    }
}
