using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace ZycyUtility
{
    public static class CvUtility
    {

        public static IEnumerable<Point2d> GetCross(this IEnumerable<((double x1, double y1, double x2, double y2), (double x1, double y1, double x2, double y2))> lines)
        {
            var newBuffer = new List<Point2d>();
            foreach (var line in lines)
            {
                (double x, double y) p1 = (line.Item1.x1, line.Item1.y1);
                (double x, double y) p2 = (line.Item1.x2, line.Item1.y2);
                (double x, double y) q1 = (line.Item2.x1, line.Item2.y1);
                (double x, double y) q2 = (line.Item2.x2, line.Item2.y2);
                try
                {
                    if (p1 == p2 || q1 == q2)
                    {
                        Debug.WriteLine($"重複点 {line.Item1} {line.Item2}");
                        continue;
                    }

                    // 傾き
                    var kp = (p2.x - p1.x) == 0
                        ? int.MaxValue
                        : (p2.y - p1.y) / (p2.x - p1.x);
                    var kq = (q2.x - q1.x) == 0
                        ? int.MaxValue
                        : (q2.y - q1.y) / (q2.x - q1.x);
                    if (kp == kq)
                    {
                        continue; // 平行
                    }

                    // yオフセット
                    var cp = kp * (0 - p1.x) + p1.y;
                    var cq = kq * (0 - q1.x) + q1.y;

                    // 交点
                    //     0 = kp * (x - 0) - (y - cp)
                    //  -) 0 = kq * (x - 0) - (y - cq)
                    // -------------------------
                    //  0 = (kp - kq) * x + (cp - cq)
                    var x = -(cp - cq) / (kp - kq);
                    var y = kp * (x - 0) + cp;

                    if (Math.Abs(x) > 10000 || Math.Abs(y) > 10000)
                    {
                        continue; // やたら遠いものは平行とみなす
                    }

                    newBuffer.Add(new Point2d(x, y));
                }
                catch (ArithmeticException)
                {
                    continue;
                }
            }
            return newBuffer;
        }

        public static IList<LineSegmentPolar> DistinctSimmiler(this IList<LineSegmentPolar> source, double distance, double degree)
        {
            List<LineSegmentPolar> newBuffer = new List<LineSegmentPolar>();
            for (int i = 0; i < source.Count; i++)
            {
                var target = source[i];
                bool isExistSimmler = newBuffer.Any(ls =>
                {
                    // rhoは原点から直線までの距離
                    var rho = Math.Abs(Math.Abs(target.Rho) - Math.Abs(ls.Rho));
                    var thetaA = Math.Min(Math.PI - Math.Abs(target.Theta % Math.PI), Math.Abs(target.Theta % Math.PI));
                    var thetaB = Math.Min(Math.PI - Math.Abs(ls.Theta % Math.PI), Math.Abs(ls.Theta % Math.PI));
                    var theta = Math.Abs(thetaA - thetaB);
                    bool rhoSimmiler = rho < distance;
                    bool thetaSimmler = theta < Math.PI / 180 * degree;
                    return rhoSimmiler && thetaSimmler;
                });

                if (!isExistSimmler)
                {
                    newBuffer.Add(target);
                }
            }

            return newBuffer;
        }

        public static IList<LineSegmentPolar> IgnoreDiagonally(this IList<LineSegmentPolar> source, double degree)
        {
            var newBuffer = new List<LineSegmentPolar>();
            foreach (var line in source)
            {
                var theta = Math.Min((Math.PI / 2) - Math.Abs(line.Theta % (Math.PI / 2)), Math.Abs(line.Theta % (Math.PI / 2)));
                if (theta < Math.PI / 180 * degree)
                {
                    newBuffer.Add(line);
                }
            }
            return newBuffer;
        }

        public static IEnumerable<IList<LineSegmentPolar>> SearchHouhLines(this Mat src, int detectEdgeCount)
        {
            var linesSet = new List<IList<LineSegmentPolar>>();
            for (int min = 0, max = src.Width, counter = 0; min + 1 < max; counter++)
            {
                var center = (min + max) / 2;
                // 距離分解能,角度分解能,閾値,線分の最小長さ,2点が同一線分上にあると見なす場合に許容される最大距離
                IList<LineSegmentPolar> _lines = src.HoughLines(1, Math.PI / 360, center, 0, 5);
                linesSet.Add(_lines);
                Debug.WriteLine($"{counter} {min}-{center}-{max} {_lines.Count}({_lines.Count})");
                if (_lines.Count == detectEdgeCount)
                {
                    break;
                }
                else if (_lines.Count > detectEdgeCount)
                {
                    min = center; // 閾値が甘すぎるので大きくする
                }
                else
                {
                    max = center; // 閾値が渋すぎたので小さくする
                }
            }

            return linesSet.OrderBy(ls => ls.Count);
        }

        public static (double x1, double y1, double x2, double y2) To2Point(this LineSegmentPolar line)
        {
            var a = Math.Cos(line.Theta);
            var b = Math.Sin(line.Theta);
            var x0 = a * line.Rho;
            var y0 = b * line.Rho;
            var x1 = x0 + 1000 * (-b);
            var y1 = y0 + 1000 * (a);
            var x2 = x0 - 1000 * (-b);
            var y2 = y0 - 1000 * (a);
            return (x1, y1, x2, y2);
        }

        public static Mat TrimAndFitBy4Cross(this Mat mat, Point2f[] crosses)
        {
            // 左上始点の時計回りに双方を合わせる
            var srcRectSum = crosses.Sum((c0, c1) => c0 + c1);
            (double x, double y) = (srcRectSum.X / crosses.Length, srcRectSum.Y / crosses.Length);
            var srcRectPoints = new Point2f[]
            {
                crosses.First(c => c.X < x && c.Y < y), crosses.First(c => c.X > x && c.Y < y),
                crosses.First(c => c.X > x && c.Y > y), crosses.First(c => c.X < x && c.Y > y),
            };
            var dstRectPoints = new Point2f[]
                { new Point2f(0, 0), new Point2f(mat.Width, 0), new Point2f(mat.Width, mat.Height), new Point2f(0, mat.Height), };
            
            var matrix = Cv2.GetPerspectiveTransform(srcRectPoints, dstRectPoints);
            var parspective = mat.Clone();
            Cv2.WarpPerspective(mat, parspective, matrix, parspective.Size());
            return parspective;
        }

        public static IEnumerable<Rect> GetTouchWallEdges(this Mat dilate)
        {
            var edgeRects = new[]
            {
                new Rect(0, 0, dilate.Width, 1),
                new Rect(0, 0, 1, dilate.Height),
                new Rect(0, dilate.Height - 1, dilate.Width, 1),
                new Rect(dilate.Width - 1, 0, 1, dilate.Height),
            };
            return edgeRects.Where(rect => dilate.Clone(rect).IsPainted());
        }

        public static Point2f To2f(this Point2d d) => new Point2f((float)d.X, (float)d.Y);
    
        public static (double min, double max) GetMinMax(this Mat mat)
        {
            mat.MinMaxIdx(out var _min, out var _max);
            return (_min, _max);
        }

        public static double GetMedium(this Mat mat)
        {
            var (min, max) = mat.GetMinMax();
            return (min + max) / 2;
        }

        public static bool IsPainted(this Mat mat)
        {
            var (min, max) = mat.GetMinMax();
            return min != max;
        }

        public static IEnumerable<Point2f> Filter4Corner(IEnumerable<Point2f> crosses, Size size)
        {
            var regulered = crosses
                .Select(c => new Point2d(c.X / size.Width, c.Y / size.Height)).ToList()
                .Where(c => c.X >= -0.1 && c.X <= 1.1 && c.Y >= -0.1 && c.Y <= 1.1);
            var center = new Point2d(
                x: (regulered.Select(p => p.X).Max() + regulered.Select(p => p.X).Min()) / 2,
                y: (regulered.Select(p => p.Y).Max() + regulered.Select(p => p.Y).Min()) / 2);
            var mostFar4 = regulered.OrderByDescending(p => Math.Abs(p.X - center.X) + Math.Abs(p.Y - center.Y)).Take(4);
            return mostFar4.Select(c => new Point2f((float)(c.X * size.Width), (float)(c.Y * size.Height)));
        }
    }

}
