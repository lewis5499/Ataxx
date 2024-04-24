using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Ataxx
{
    class DrawGraph
    {
        //下面是画棋盘用的
        int Row = 7;
        int Column = 7;
        int squ = 81;//294 / 7=42

        public void DrawBoard(ref Graphics g)
        {
            for (int rowIndex = 0; rowIndex < Row; rowIndex++)//外层控制行数
            {
                for (int colIndex = 0; colIndex < Column; colIndex++)//内层循环控制每行的列数
                {
                    g.DrawRectangle(Pens.Black, new Rectangle(50 + colIndex * squ, 50 + rowIndex * squ, squ, squ));
                    g.FillRectangle(Brushes.BurlyWood, new Rectangle(50 + colIndex * squ + 1, 50 + rowIndex * squ + 1, squ - 1, squ - 1));
                }
            }
        }//画棋盘的方法    
        public void Draw(ref Graphics g, int[,] Array, int x, int y)
        {
            if (Array[x, y] == 0)
            {
                g.FillRectangle(Brushes.BurlyWood, new Rectangle(x * squ + 51, y * squ + 51, squ - 1, squ - 1));
            }
            else if (Array[x, y] == 1)
            {
                g.FillEllipse(Brushes.White, new Rectangle(51 + squ * x, 51 + squ * y, squ - 2, squ - 2));
            }
            else if (Array[x, y] == 2)
            {
                g.FillEllipse(Brushes.Black, new Rectangle(x * squ + 51, y * squ + 51, squ - 2, squ - 2));
            }

            else if (Array[x, y] == 3)
            {
                g.FillRectangle(Brushes.PaleGoldenrod, new Rectangle(x * squ + 51, y * squ + 51, squ - 1, squ - 1));
            }
            else if (Array[x, y] == 4)
            {
                g.FillRectangle(Brushes.PaleGreen, new Rectangle(x * squ + 51, y * squ + 51, squ - 1, squ - 1));
            }
        }//坐标值为0则清空,坐标值为1则画白格，坐标值为2则画黑格，坐标值为3则画淡橘黄色，坐标值为4画淡绿色
    }
}
