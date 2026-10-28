using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing; // 确保引用了 System.Drawing

using Rhino;
using Rhino.Geometry;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

// 注意：如果你修改了项目的默认命名空间，请同步修改这里。
// 如果没改，保持 Sliding_Simulation 即可，这不影响生成的 .gha 文件名。
namespace Sliding_Simulation
{
    public class CurveToArcsComponent : GH_Component
    {
        /// <summary>
        /// 构造函数：定义电池名称、昵称、描述、分类
        /// </summary>
        public CurveToArcsComponent()
          : base("Curve to Biarcs", "Crv2Arcs",
              "屎壳郎2号：将任意曲线转化为高质量圆弧段 (G1切线连续)。\n" +
              "核心特性：死弯自动修复、A-极速拟合内核。",
              "Dung Beetle", // 主分类 (和你的滑行模拟保持一致)
              "Geometry")    // 子分类 (建议区分开，显得专业)
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C",
                "需要拟合的原始曲线 (NURBS/Polyline)",
                GH_ParamAccess.item);

            pManager.AddNumberParameter("Tolerance", "Tol",
                "拟合公差 (最大允许偏差)\n" +
                "-----------------------\n" +
                "定义圆弧与原曲线的最大偏离距离。\n" +
                "• 注意：公差越小，逼近越准，但生成的圆弧碎段会显著增多。",
                GH_ParamAccess.item, 0.01);

            pManager.AddNumberParameter("Angle Tolerance", "Ang",
                "死弯角度阈值 (度)\n" +
                "-----------------------\n" +
                "超过此折角的接缝会被视为“死弯”并触发优化机制。\n" +
                "• 用于排查加工时（如拉弯、滚圆）无法平滑过渡的节点。",
                GH_ParamAccess.item, 5.0);

            pManager.AddNumberParameter("Max Length", "MaxL",
                "圆弧最大长度限制\n" +
                "-----------------------\n" +
                "限制单段圆弧的最大弧长。\n" +
                "💡 制造提示：通常需参考原材料（如钢管、型材）的采购定尺或运输极限。",
                GH_ParamAccess.item, 1000.0);

            pManager.AddNumberParameter("Min Length", "MinL",
                "圆弧最小长度限制\n" +
                "-----------------------\n" +
                "防止生成无法加工的极端碎段。\n" +
                "💡 制造提示：应大于数控设备的最小进给步长或最小操作余量。",
                GH_ParamAccess.item, 100.0);

            pManager.AddIntegerParameter("Resolution", "Res",
                "拟合精度 (采样步数)\n" +
                "-----------------------\n" +
                "底层曲率采样密度，默认 50-100 足以应对大多数情况。",
                GH_ParamAccess.item, 50);

            pManager.AddBooleanParameter("Seam Optimize", "Seam",
                "闭合曲线接缝优化\n" +
                "尝试自动将接缝移动到曲率平缓处，以减少闭合环的加工畸变。",
                GH_ParamAccess.item, true);

            pManager.AddBooleanParameter("Dir Optimize", "Dir",
                "方向优化\n" +
                "尝试正反双向拟合比对，自动选择误差更小、段数更少的优解。",
                GH_ParamAccess.item, true);

            pManager.AddBooleanParameter("Mid Optimize", "Mid",
                "中点强制优化\n提高圆弧中段对原曲线的贴合度。",
                GH_ParamAccess.item, true);

            pManager.AddBooleanParameter("Opt Shorts", "O_S",
                "优化短弧\n" +
                "尝试吞并或融合低于 [Min Length] 的加工废段。",
                GH_ParamAccess.item, true);

            pManager.AddBooleanParameter("Opt Kinks", "O_K",
                "优化死弯 (核心功能)\n" +
                "自动打散并重构不光顺的折角，确保整条曲线 G1 连续，降低后期打磨成本。",
                GH_ParamAccess.item, false);

            pManager.AddIntegerParameter("Kink Iters", "Iter",
                "死弯优化迭代次数\n" +
                "处理复杂连续死弯时的循环重构次数。",
                GH_ParamAccess.item, 5);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddArcParameter("Arcs", "A",
                "优化后的圆弧段 (Arcs)\n可直接用于导出加工图纸。",
                GH_ParamAccess.list);

            pManager.AddNumberParameter("Deviations", "Dev",
                "每段圆弧的最大偏差值\n用于最终质量精度复核。",
                GH_ParamAccess.list);

            pManager.AddVectorParameter("Tangents", "Tan", "起始切线向量", GH_ParamAccess.list);

            pManager.AddLineParameter("DevLines", "DL",
                "偏差可视化线段\n连接圆弧与原曲线误差最大处的指示线，便于肉眼检查。",
                GH_ParamAccess.list);

            pManager.AddTextParameter("Log", "Msg",
                "诊断报告与统计信息\n包含分段统计、极值报告及报错信息。",
                GH_ParamAccess.item);

            pManager.AddArcParameter("Shorts", "S_Arc",
                "残留的短弧 ⚠️\n未能被优化的极短弧，可能低于加工极限，需人工确认。",
                GH_ParamAccess.list);

            pManager.AddPointParameter("Joints", "J_Pt", "所有分段接缝点", GH_ParamAccess.list);

            pManager.AddNumberParameter("Angles", "J_Ang",
                "接缝处折角 (度)\n用于评估相邻圆弧过渡的平滑性。",
                GH_ParamAccess.list);

            pManager.AddPointParameter("Kinks", "K_Pt",
                "残留死弯位置 🔴\n未能修复的加工高风险点，可能需要回 Rhino 修改原线。",
                GH_ParamAccess.list);

            pManager.AddNumberParameter("KinkAngles", "K_Ang", "残留死弯角度", GH_ParamAccess.list);

            pManager.AddCurveParameter("Debug", "Dbg", "调试：被死弯优化系统切除重构的原始局部片段", GH_ParamAccess.list);
        }


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve crv = null;
            double tolerance = 0.01;
            double angle_tolerance = 5.0;
            double max_len = 1000.0;
            double min_len = 100.0;
            int resolution = 50;
            bool seam_optimize = true;
            bool dir_optimize = true;
            bool midpoint_optimize = true;
            bool optimize_shorts = true;
            bool optimize_kinks = true;
            int kink_iters = 5;

            if (!DA.GetData(0, ref crv)) return;
            DA.GetData(1, ref tolerance);
            DA.GetData(2, ref angle_tolerance);
            DA.GetData(3, ref max_len);
            DA.GetData(4, ref min_len);
            DA.GetData(5, ref resolution);
            DA.GetData(6, ref seam_optimize);
            DA.GetData(7, ref dir_optimize);
            DA.GetData(8, ref midpoint_optimize);
            DA.GetData(9, ref optimize_shorts);
            DA.GetData(10, ref optimize_kinks);
            DA.GetData(11, ref kink_iters);

            if (crv == null) return;

            List<Arc> res_arcs = new List<Arc>();
            List<double> ds_f = new List<double>();
            List<Vector3d> out_vecs = new List<Vector3d>();
            List<Line> ls_f = new List<Line>();
            List<string> msg_lines = new List<string>();
            List<Arc> short_arcs = new List<Arc>();
            List<Point3d> joint_pts = new List<Point3d>();
            List<double> joint_angles = new List<double>();
            List<Point3d> kink_pts = new List<Point3d>();
            List<double> kink_angles = new List<double>();
            List<Curve> debug_raw_segs = new List<Curve>();

            double ang_tol_rad = RhinoMath.ToRadians(Math.Abs(angle_tolerance));
            int internal_passes = 8;

            Curve w_curve = crv.DuplicateCurve();
            double totalLen = w_curve.GetLength();
            if (totalLen <= 1e-9) { DA.SetData(4, "❌ 曲线长度过短/无效。"); return; }

            w_curve.Domain = new Interval(0, totalLen);
            bool isClosed = w_curve.IsClosed;
            bool wasSeamMoved = false;
            bool wasReversed = false;

            try
            {
                if (seam_optimize && isClosed)
                {
                    int samples = 72; double max_k = -1.0; double best_t = 0.0;
                    for (int k = 0; k < samples; k++)
                    {
                        double t = (double)k / samples * totalLen;
                        double k_v = 0.0;
                        try { Vector3d cv = w_curve.CurvatureAt(t); if (cv.IsValid) k_v = cv.Length; } catch { k_v = 0.0; }
                        if (k_v > max_k) { max_k = k_v; best_t = t; }
                    }
                    best_t = Math.Max(w_curve.Domain.Min, Math.Min(w_curve.Domain.Max, best_t));
                    if (best_t > 0.001)
                    {
                        w_curve.ChangeClosedCurveSeam(best_t); w_curve.Domain = new Interval(0, totalLen); wasSeamMoved = true;
                    }
                }

                List<double> ts_f;
                res_arcs = SolveMethodLimitedForceClose(w_curve, tolerance, ang_tol_rad, max_len, min_len, resolution, midpoint_optimize, out ds_f, out ls_f, out ts_f);

                if (dir_optimize)
                {
                    Curve crv_rev = w_curve.DuplicateCurve(); crv_rev.Reverse(); crv_rev.Domain = new Interval(0, totalLen);
                    List<double> d_rev, ts_rev; List<Line> l_rev;
                    List<Arc> arcs_rev = SolveMethodLimitedForceClose(crv_rev, tolerance, ang_tol_rad, max_len, min_len, resolution, midpoint_optimize, out d_rev, out l_rev, out ts_rev);
                    if (arcs_rev.Count < res_arcs.Count || (arcs_rev.Count == res_arcs.Count && Average(d_rev) < Average(ds_f)))
                    {
                        wasReversed = true; res_arcs = arcs_rev; ds_f = d_rev; ls_f = l_rev; ts_f = ts_rev; w_curve = crv_rev;
                    }
                }

                if (res_arcs.Count > 1) res_arcs = OptimizeReduceSegments(w_curve, res_arcs, ds_f, ls_f, ts_f, tolerance, max_len, isClosed, out ds_f, out ls_f, out ts_f);
                if (optimize_shorts) res_arcs = AbsoluteLengthOptimize(w_curve, res_arcs, ds_f, ls_f, ts_f, min_len, max_len, tolerance, midpoint_optimize, internal_passes, out ds_f, out ls_f);

                int iters = kink_iters; int failBudgetMax = 6;
                List<Tuple<int, int>> kinkClusters = null; List<Curve> finalDbgSegs = new List<Curve>(); List<Point3d> finalDbgPts = new List<Point3d>();
                List<int> ptsBeforeSeq = new List<int>(); List<int> ptsAfterSeq = new List<int>(); List<double> usedMaxSeq = new List<double>(); List<double> usedMinSeq = new List<double>(); List<double> kSeq = new List<double>();
                int roundsUsed = 0; int failBudget = 0; int fbMax = 0; int failStreak = 0;

                if (!optimize_kinks)
                {
                    List<Curve> dbgSegs; List<Point3d> dbgPts;
                    ScanKinksAndExtractRawSegs(w_curve, res_arcs, angle_tolerance, isClosed, out kinkClusters, out dbgSegs, out dbgPts);
                    if (dbgSegs != null) finalDbgSegs.AddRange(dbgSegs); if (dbgPts != null) finalDbgPts.AddRange(dbgPts);
                    ptsBeforeSeq.Add(finalDbgPts.Count); ptsAfterSeq.Add(finalDbgPts.Count);
                }
                else
                {
                    for (int iter = 0; iter < iters; iter++)
                    {
                        List<Curve> dbgSegs; List<Point3d> dbgPts;
                        ScanKinksAndExtractRawSegs(w_curve, res_arcs, angle_tolerance, isClosed, out kinkClusters, out dbgSegs, out dbgPts);
                        finalDbgSegs.Clear(); finalDbgPts.Clear();
                        if (dbgSegs != null) finalDbgSegs.AddRange(dbgSegs); if (dbgPts != null) finalDbgPts.AddRange(dbgPts);
                        ptsBeforeSeq.Add(finalDbgPts.Count);

                        if (kinkClusters == null || kinkClusters.Count == 0) { ptsAfterSeq.Add(finalDbgPts.Count); roundsUsed = iter; break; }

                        int replaced = 0; double usedMaxLIt = max_len; double usedMinLIt = min_len;
                        ApplyKinkClustersRebuild(w_curve, ref res_arcs, kinkClusters, tolerance, ang_tol_rad, angle_tolerance, max_len, min_len, iter, failBudget, resolution, midpoint_optimize, dir_optimize, optimize_shorts, isClosed, out replaced, out usedMaxLIt, out usedMinLIt);

                        usedMaxSeq.Add(usedMaxLIt); usedMinSeq.Add(usedMinLIt);
                        double kUsed = 3.0 - 0.2 * failBudget; if (kUsed < 2.0) kUsed = 2.0; kSeq.Add(kUsed);

                        {
                            List<Curve> dbgSegs_after; List<Point3d> dbgPts_after; List<Tuple<int, int>> kinkClusters_after;
                            ScanKinksAndExtractRawSegs(w_curve, res_arcs, angle_tolerance, isClosed, out kinkClusters_after, out dbgSegs_after, out dbgPts_after);
                            ptsAfterSeq.Add(dbgPts_after != null ? dbgPts_after.Count : 0);
                        }

                        int ptsBefore = ptsBeforeSeq[ptsBeforeSeq.Count - 1]; int ptsAfter = ptsAfterSeq[ptsAfterSeq.Count - 1];
                        if (ptsAfter == 0) { failBudget = 0; failStreak = 0; roundsUsed = iter + 1; }
                        else if (ptsAfter < ptsBefore) { failBudget = Math.Max(0, failBudget - 2); failStreak = 0; roundsUsed = iter + 1; }
                        else { failStreak++; failBudget = Math.Min(failBudgetMax, failBudget + 1); if (failBudget > fbMax) fbMax = failBudget; if (failStreak >= 10) { roundsUsed = iter; break; } }
                    }
                }

                {
                    List<Curve> dbgSegs2; List<Point3d> dbgPts2; List<Tuple<int, int>> clusters2;
                    ScanKinksAndExtractRawSegs(w_curve, res_arcs, angle_tolerance, isClosed, out clusters2, out dbgSegs2, out dbgPts2);
                    finalDbgSegs.Clear(); finalDbgPts.Clear(); if (dbgSegs2 != null) finalDbgSegs.AddRange(dbgSegs2); if (dbgPts2 != null) finalDbgPts.AddRange(dbgPts2);
                }
                debug_raw_segs = finalDbgSegs;

                if (res_arcs.Count > 0) RecalculateFinalStats_Fast(w_curve, res_arcs, out ds_f, out ls_f, isClosed);
                else { ds_f = new List<double>(); ls_f = new List<Line>(); }

                if (wasReversed)
                {
                    res_arcs.Reverse(); ds_f.Reverse(); ls_f.Reverse();
                    List<Arc> flipped = new List<Arc>(); foreach (Arc a in res_arcs) { Arc tmp = a; tmp.Reverse(); flipped.Add(tmp); }
                    res_arcs = flipped;
                }

                int kinkCount = 0; double maxKinkDeg = 0; int shortCount = 0;
                for (int j = 0; j < res_arcs.Count; j++)
                {
                    if (res_arcs[j].Length < min_len - 1e-3) { shortCount++; short_arcs.Add(res_arcs[j]); }
                    if (j < res_arcs.Count - 1)
                    {
                        double angleDeg = GetTangentAngle(res_arcs[j], res_arcs[j + 1]);
                        joint_pts.Add(res_arcs[j].EndPoint); joint_angles.Add(angleDeg);
                        if (angleDeg > angle_tolerance + 0.05) { kinkCount++; maxKinkDeg = Math.Max(maxKinkDeg, angleDeg); kink_pts.Add(res_arcs[j].EndPoint); kink_angles.Add(angleDeg); }
                    }
                }
                if (isClosed && res_arcs.Count > 1)
                {
                    if (res_arcs.Last().EndPoint.DistanceTo(res_arcs.First().StartPoint) < 0.001)
                    {
                        double loopAngleDeg = GetTangentAngle(res_arcs.Last(), res_arcs.First());
                        joint_pts.Add(res_arcs.Last().EndPoint); joint_angles.Add(loopAngleDeg);
                        if (loopAngleDeg > angle_tolerance + 0.05) { kinkCount++; maxKinkDeg = Math.Max(maxKinkDeg, loopAngleDeg); kink_pts.Add(res_arcs.Last().EndPoint); kink_angles.Add(loopAngleDeg); }
                    }
                }

                string kinkIterLog = !optimize_kinks ? $"kink:off | pts:{ptsBeforeSeq.FirstOrDefault()}" : $"kink:{roundsUsed}r | fbMax:{fbMax}";
                double maxDevVal = (ds_f.Count > 0) ? ds_f.Max() : 0.0;
                string finalAuditMsg = ""; if (kinkCount > 0) finalAuditMsg += $"🔴死弯({maxKinkDeg:F1}°)"; if (shortCount > 0) finalAuditMsg += $" ⚠️短弧({shortCount}处)";
                string logStr = $"分段:{res_arcs.Count} | 接缝:{(wasSeamMoved ? "已移" : "固定")} | 方向:{(wasReversed ? "反向" : "原向")}\n" +
                                $"偏差:{maxDevVal:F3}mm {(maxDevVal <= tolerance ? "✅" : "❌")}\n" +
                                $"死弯:{(kinkCount > 0 ? "有" : "无")} | 短弧:{(shortCount > 0 ? "有" : "无")}\n" +
                                $"{finalAuditMsg}\n[{kinkIterLog}]";
                msg_lines.Add(logStr);
                if (res_arcs.Count > 0) out_vecs.Add(res_arcs[0].TangentAt(0));
            }
            catch (Exception e) { msg_lines.Add("错误: " + e.Message); }

            DA.SetDataList(0, res_arcs); DA.SetDataList(1, ds_f); DA.SetDataList(2, out_vecs); DA.SetDataList(3, ls_f);
            DA.SetData(4, string.Join("\n", msg_lines)); DA.SetDataList(5, short_arcs); DA.SetDataList(6, joint_pts);
            DA.SetDataList(7, joint_angles); DA.SetDataList(8, kink_pts); DA.SetDataList(9, kink_angles); DA.SetDataList(10, debug_raw_segs);
        }

        // ======================= 🔒 核心算法内核 =======================

        private List<Arc> SolveMethodLimitedForceClose(Curve c, double tol, double angTol, double maxL, double minL, int res, bool opt, out List<double> ds, out List<Line> ls, out List<double> ts)
        {
            ds = new List<double>(); ls = new List<Line>(); ts = new List<double>() { 0.0 }; List<Arc> results = new List<Arc>();
            double ct = 0.0; double tl = c.Domain.Max; Vector3d pT; try { pT = c.TangentAtStart; } catch { pT = Vector3d.XAxis; }
            int step = (res > 0) ? res : 20; double absoluteMinLen = Math.Max(0.1, minL * 0.8); int safety = 0;
            while (ct < tl - 1e-4)
            {
                if (++safety > 200000) break; double rem = tl - ct; bool foundValid = false;
                Arc chosenArc = Arc.Unset; double chosenDev = 0; Line chosenLine = Line.Unset; double chosenT = ct; Arc bestBadArc = Arc.Unset; double minBadDev = 1e100; Line bestBadLine = Line.Unset; double bestBadT = ct;
                double dynamicMaxL = maxL; double startLen = Math.Min(rem, dynamicMaxL); double midLen = Math.Min(rem, minL);
                if (startLen > midLen + 0.1)
                {
                    int bigSteps = (int)((startLen - midLen) / (maxL / Math.Max(1, step))) + 2;
                    for (int i = 0; i <= bigSteps; i++)
                    {
                        double sl = startLen - (double)i * (startLen - midLen) / bigSteps; if (sl < midLen) sl = midLen; double tt = ct + sl;
                        double d; Line l; Arc a = tryFitRaw(c, ct, tt, 1e10, opt, out d, out l);
                        if (a.IsValid)
                        {
                            if (a.Length > dynamicMaxL + 0.001) continue; if (d < minBadDev) { minBadDev = d; bestBadArc = a; bestBadLine = l; bestBadT = tt; }
                            double angleDiff = Vector3d.VectorAngle(pT, GetArcTangent(a, true)); if (d <= tol && angleDiff <= angTol) { chosenArc = a; chosenDev = d; chosenLine = l; chosenT = tt; foundValid = true; break; }
                        }
                    }
                }
                if (!foundValid && rem > absoluteMinLen)
                {
                    double fallbackStart = (rem < minL) ? rem : minL; double fallbackEnd = (rem < absoluteMinLen) ? rem : absoluteMinLen; int smallSteps = 10;
                    for (int i = 1; i <= smallSteps; i++)
                    {
                        double sl = fallbackStart - (double)i * (fallbackStart - fallbackEnd) / smallSteps; if (sl < fallbackEnd) sl = fallbackEnd; double tt = ct + sl;
                        double d; Line l; Arc a = tryFitRaw(c, ct, tt, 1e10, opt, out d, out l);
                        if (a.IsValid)
                        {
                            if (a.Length > dynamicMaxL + 0.001) continue; if (d < minBadDev) { minBadDev = d; bestBadArc = a; bestBadLine = l; bestBadT = tt; }
                            double angleDiff = Vector3d.VectorAngle(pT, GetArcTangent(a, true)); if (d <= tol && angleDiff <= angTol) { chosenArc = a; chosenDev = d; chosenLine = l; chosenT = tt; foundValid = true; break; }
                        }
                    }
                }
                double prevCt = ct;
                if (foundValid) { results.Add(chosenArc); ds.Add(chosenDev); ls.Add(chosenLine); ts.Add(chosenT); ct = chosenT; pT = GetArcTangent(chosenArc, false); }
                else if (bestBadArc.IsValid) { results.Add(bestBadArc); ds.Add(minBadDev); ls.Add(bestBadLine); ts.Add(bestBadT); ct = bestBadT; pT = GetArcTangent(bestBadArc, false); }
                else
                {
                    double d; Line l; Arc endArc = tryFitRaw(c, ct, tl, 1e10, opt, out d, out l);
                    if (endArc.IsValid && endArc.Length <= dynamicMaxL + 0.1) { results.Add(endArc); ds.Add(d); ls.Add(l); ts.Add(tl); ct = tl; pT = GetArcTangent(endArc, false); }
                    else
                    {
                        Point3d p0 = c.PointAt(ct); Point3d p1 = c.PointAt(tl); if (p0.DistanceTo(p1) < 1e-4) { ct = tl; break; }
                        Point3d m = (p0 + p1) / 2;
                        Vector3d dir = p1 - p0; if (dir.Length < 1e-10) dir = Vector3d.XAxis; Vector3d perp = Vector3d.CrossProduct(dir, Vector3d.ZAxis); if (perp.Length < 1e-10) perp = Vector3d.CrossProduct(dir, Vector3d.YAxis); perp.Unitize(); m += perp * 0.0001;
                        Arc flatArc = new Arc(p0, m, p1); results.Add(flatArc); ds.Add(0); ls.Add(new Line(p0, p1)); ts.Add(tl); ct = tl; pT = GetArcTangent(flatArc, false);
                    }
                }
                if (ct <= prevCt + 1e-9) { ct = Math.Min(tl, prevCt + Math.Max(absoluteMinLen, 0.5)); if (ts.Count > 0) ts[ts.Count - 1] = ct; }
            }
            return results;
        }

        private List<Arc> OptimizeReduceSegments(Curve crv, List<Arc> arcs, List<double> devs, List<Line> lines, List<double> ts, double tol, double maxL, bool isClosed, out List<double> oDs, out List<Line> oLs, out List<double> oTs)
        {
            List<Arc> rA = new List<Arc>(arcs); List<double> rD = new List<double>(devs); List<Line> rL = new List<Line>(lines); List<double> rT = new List<double>(ts);
            bool changed = true; int pass = 0;
            while (changed && pass < 10)
            {
                changed = false; pass++;
                for (int i = 0; i <= rA.Count - 2; i++)
                {
                    if (i + 2 >= rT.Count) break; double tStart = rT[i]; double tEnd = rT[i + 2]; double d; Line l;
                    Arc merged = tryFitRaw(crv, tStart, tEnd, 1e10, true, out d, out l);
                    if (merged.IsValid) { if (merged.Length > maxL + 0.001) continue; if (d <= tol) { rA[i] = merged; rD[i] = d; rL[i] = l; rA.RemoveAt(i + 1); rD.RemoveAt(i + 1); rL.RemoveAt(i + 1); rT.RemoveAt(i + 1); changed = true; if (i > 0) i -= 2; } }
                }
            }
            oDs = rD; oLs = rL; oTs = rT; return rA;
        }

        private double GetMaxDev_Fast(Curve c, Arc a, double t0, double t1, double limit, out Line l)
        {
            l = Line.Unset; double mD = 0; int steps = 24; double totalDom = (t1 >= t0) ? (t1 - t0) : (c.Domain.Max - t0 + t1 - c.Domain.Min);
            for (int i = 0; i <= steps; i++)
            {
                double f = (double)i / steps; double t = (t1 >= t0) ? (t0 + f * totalDom) : ((t0 + f * totalDom <= c.Domain.Max) ? (t0 + f * totalDom) : (c.Domain.Min + (f * totalDom - (c.Domain.Max - t0))));
                Point3d pt = c.PointAt(t); Point3d pa;
                if (!TryClosestPointOnArc(a, pt, out pa))
                {
                    Point3d pA0 = a.StartPoint; Point3d pA1 = a.EndPoint; Point3d paFallback = (pt.DistanceTo(pA0) <= pt.DistanceTo(pA1)) ? pA0 : pA1; double dFallback = pt.DistanceTo(paFallback);
                    if (dFallback > mD) { mD = dFallback; l = new Line(pt, paFallback); }
                    if (mD > limit) return mD; continue;
                }
                double d = pt.DistanceTo(pa); if (d > mD) { mD = d; l = new Line(pt, pa); }
                if (mD > limit) return mD;
            }
            return mD;
        }

        private void RecalculateFinalStats_Fast(Curve crv, List<Arc> arcs, out List<double> finalDevs, out List<Line> finalLines, bool isClosed)
        {
            finalDevs = new List<double>(); finalLines = new List<Line>(); List<double> ts = new List<double>();
            if (arcs.Count == 0) return; double t0; crv.ClosestPoint(arcs[0].StartPoint, out t0); ts.Add(t0);
            foreach (Arc a in arcs) { double t; crv.ClosestPoint(a.EndPoint, out t); ts.Add(t); }
            if (isClosed && Math.Abs(ts[0] - crv.Domain.Min) < 0.001 && Math.Abs(ts.Last() - crv.Domain.Min) < 0.001) ts[ts.Count - 1] = crv.Domain.Max;
            for (int i = 0; i < arcs.Count; i++) { Line l; double d = GetMaxDev_Fast(crv, arcs[i], ts[i], ts[i + 1], double.MaxValue, out l); finalDevs.Add(d); finalLines.Add(l); }
        }

        private Arc tryFitRaw(Curve c, double t0, double t1, double tol, bool opt, out double d, out Line l)
        {
            d = 1e9; l = Line.Unset; Arc b = Arc.Unset;
            double[] rs = opt ? new double[] { 0.5, 0.4, 0.6, 0.45, 0.55, 0.35, 0.65, 0.3, 0.7 } : new double[] { 0.5 };
            double totalDom = (t1 >= t0) ? (t1 - t0) : (c.Domain.Max - t0 + t1 - c.Domain.Min);
            foreach (double r in rs)
            {
                double tmRaw = t0 + totalDom * r; double tm = (tmRaw <= c.Domain.Max) ? tmRaw : (c.Domain.Min + (tmRaw - c.Domain.Max));
                Arc a = new Arc(c.PointAt(t0), c.PointAt(tm), c.PointAt(t1));
                if (a.IsValid) { double cd = GetMaxDev_Fast(c, a, t0, t1, d, out Line cl); if (cd <= tol && cd < d) { d = cd; b = a; l = cl; } }
            }
            if (!b.IsValid)
            {
                Point3d p0 = c.PointAt(t0); Point3d p1 = c.PointAt(t1);
                if (p0.DistanceTo(p1) > 1e-4)
                {
                    Point3d m = (p0 + p1) / 2; Vector3d dir = p1 - p0; if (dir.Length < 1e-10) dir = Vector3d.XAxis;
                    Vector3d perp = Vector3d.CrossProduct(dir, Vector3d.ZAxis); if (perp.Length < 1e-10) perp = Vector3d.CrossProduct(dir, Vector3d.YAxis); perp.Unitize(); m += perp * 0.0001;
                    Arc fA = new Arc(p0, m, p1); if (fA.IsValid) { double cd = GetMaxDev_Fast(c, fA, t0, t1, d, out Line cl); if (cd <= tol && cd < d) { d = cd; b = fA; l = cl; } }
                }
            }
            return b;
        }

        private List<Arc> AbsoluteLengthOptimize(Curve crv, List<Arc> arcs, List<double> devs, List<Line> lines, List<double> ts, double minL, double maxL, double baseTol, bool opt, int passes, out List<double> oDevs, out List<Line> oLines)
        {
            List<Arc> rA = new List<Arc>(arcs); List<double> rD = new List<double>(devs); List<Line> rL = new List<Line>(lines); List<double> rT = new List<double>(ts); double infT = 1e10;
            for (int p = 0; p < passes; p++)
            {
                bool changed = false;
                for (int i = 0; i < rA.Count; i++)
                {
                    if (rA[i].Length < minL - 0.001)
                    {
                        double curL = rA[i].Length; double need = (minL * 1.05) - curL; bool ext = false; double[] rs = new double[] { 1.0, 0.8, 0.6, 0.4, 0.2 };
                        foreach (double ratio in rs)
                        {
                            double tryB = need * ratio; double tarL = curL + tryB; if (tarL <= curL + 0.1) continue; double d1, d2; Line l1, l2; Arc na1, na2;
                            if (i == 0)
                            {
                                double tS = rT[0], tNL = (rA.Count > 1) ? rT[2] : crv.Domain.Max, tT = tS + tarL;
                                if (tT < tNL - 0.05) { na1 = tryFitRaw(crv, tS, tT, infT, opt, out d1, out l1); na2 = tryFitRaw(crv, tT, tNL, infT, opt, out d2, out l2); if (na1.IsValid && na2.IsValid) { rA[0] = na1; rD[0] = d1; rL[0] = l1; rA[1] = na2; rD[1] = d2; rL[1] = l2; rT[1] = tT; changed = true; ext = true; break; } }
                            }
                            else if (i == rA.Count - 1)
                            {
                                double tE = rT[i + 1], tPS = rT[i - 1], tTS = tE - tarL; if (tTS > tPS + 0.05) { na1 = tryFitRaw(crv, tPS, tTS, infT, opt, out d1, out l1); na2 = tryFitRaw(crv, tTS, tE, infT, opt, out d2, out l2); if (na1.IsValid && na2.IsValid) { rA[i - 1] = na1; rD[i - 1] = d1; rL[i - 1] = l1; rA[i] = na2; rD[i] = d2; rL[i] = l2; rT[i] = tTS; changed = true; ext = true; break; } }
                            }
                            else
                            {
                                double tE = rT[i + 1], tTS = tE - tarL; if (tTS > rT[i - 1] + 0.05) { na1 = tryFitRaw(crv, rT[i - 1], tTS, infT, opt, out d1, out l1); na2 = tryFitRaw(crv, tTS, tE, infT, opt, out d2, out l2); if (na1.IsValid && na2.IsValid) { rA[i - 1] = na1; rD[i - 1] = d1; rL[i - 1] = l1; rA[i] = na2; rD[i] = d2; rL[i] = l2; rT[i] = tTS; changed = true; ext = true; break; } }
                            }
                        }
                        if (ext) break; bool mer = false; double d; Line l; Arc f;
                        if (i > 0) { f = tryFitRaw(crv, rT[i - 1], rT[i + 1], infT, opt, out d, out l); if (f.IsValid) { rA[i - 1] = f; rD[i - 1] = d; rL[i - 1] = l; rA.RemoveAt(i); rD.RemoveAt(i); rL.RemoveAt(i); rT.RemoveAt(i); changed = true; mer = true; i--; } }
                        if (!mer && i < rA.Count - 1) { f = tryFitRaw(crv, rT[i], rT[i + 2], infT, opt, out d, out l); if (f.IsValid) { rA[i] = f; rD[i] = d; rL[i] = l; rA.RemoveAt(i + 1); rD.RemoveAt(i + 1); rL.RemoveAt(i + 1); rT.RemoveAt(i + 1); changed = true; mer = true; } }
                        if (mer) break;
                    }
                }
                if (!changed) break;
            }
            double killLen = minL * 0.15;
            for (int p = 0; p < 2; p++)
            {
                bool killed = false;
                for (int i = 0; i < rA.Count; i++)
                {
                    if (rA[i].Length < killLen)
                    {
                        double d; Line l; Arc f;
                        if (i > 0) { f = tryFitRaw(crv, rT[i - 1], rT[i + 1], infT, opt, out d, out l); if (f.IsValid) { rA[i - 1] = f; rD[i - 1] = d; rL[i - 1] = l; rA.RemoveAt(i); rD.RemoveAt(i); rL.RemoveAt(i); rT.RemoveAt(i); killed = true; i--; continue; } }
                        if (i < rA.Count - 1) { f = tryFitRaw(crv, rT[i], rT[i + 2], infT, opt, out d, out l); if (f.IsValid) { rA[i] = f; rD[i] = d; rL[i] = l; rA.RemoveAt(i + 1); rD.RemoveAt(i + 1); rL.RemoveAt(i + 1); rT.RemoveAt(i + 1); killed = true; continue; } }
                    }
                }
                if (!killed) break;
            }
            oDevs = rD; oLines = rL; return rA;
        }

        private Vector3d GetArcTangent(Arc a, bool start) { Vector3d rad = (start ? a.StartPoint : a.EndPoint) - a.Center; Vector3d tan = Vector3d.CrossProduct(a.Plane.Normal, rad); tan.Unitize(); return tan; }
        private double GetTangentAngle(Arc a1, Arc a2) { double deg = RhinoMath.ToDegrees(Vector3d.VectorAngle(GetArcTangent(a1, false), GetArcTangent(a2, true))); return deg > 90 ? Math.Abs(180 - deg) : deg; }
        private double Average(List<double> v) { return v.Count == 0 ? 0 : v.Average(); }
        private Curve JoinArcRange(List<Arc> arcs, int a0, int a1) { List<Curve> segs = new List<Curve>(); for (int i = a0; i <= a1; i++) { Curve c = arcs[i].ToNurbsCurve(); if (c != null) segs.Add(c); } if (segs.Count == 0) return null; Curve[] joined = Curve.JoinCurves(segs, 1e-6); if (joined == null || joined.Length == 0) return null; Curve best = joined[0]; double bestL = best.GetLength(); for (int i = 1; i < joined.Length; i++) { double L = joined[i].GetLength(); if (L > bestL) { bestL = L; best = joined[i]; } } return best; }
        private Curve JoinArcIndexList(List<Arc> arcs, List<int> idx) { List<Curve> segs = new List<Curve>(); for (int i = 0; i < idx.Count; i++) { int k = idx[i]; if (k < 0 || k >= arcs.Count) continue; Curve c = arcs[k].ToNurbsCurve(); if (c != null) segs.Add(c); } if (segs.Count == 0) return null; Curve[] joined = Curve.JoinCurves(segs, 1e-6); if (joined == null || joined.Length == 0) return null; Curve best = joined[0]; double bestL = best.GetLength(); for (int i = 1; i < joined.Length; i++) { double L = joined[i].GetLength(); if (L > bestL) { bestL = L; best = joined[i]; } } return best; }
        private Point3d PointAtCurveMidByLength(Curve c) { double L = c.GetLength(); if (L <= 1e-9) return c.PointAtStart; double t; if (c.LengthParameter(L * 0.5, out t)) return c.PointAt(t); return c.PointAt((c.Domain.Min + c.Domain.Max) * 0.5); }
        private bool IsBetween(double tM, double tA, double tB) { if (tA < tB) return (tM > tA && tM < tB); return (tM > tB && tM < tA); }
        private void ScanKinksAndExtractRawSegs(Curve rawCrv, List<Arc> arcs, double angTolDeg, bool isClosed, out List<Tuple<int, int>> clusters, out List<Curve> debug_raw_segs, out List<Point3d> debug_trigger_pts)
        {
            clusters = new List<Tuple<int, int>>(); debug_raw_segs = new List<Curve>(); debug_trigger_pts = new List<Point3d>();
            if (rawCrv == null || arcs == null || arcs.Count < 2) return;
            int n = arcs.Count; double thr = angTolDeg + 0.05; bool[] bad = new bool[n]; int jointCount = isClosed ? n : (n - 1);
            for (int i = 0; i < jointCount; i++) { int j = (i + 1) % n; double a = GetTangentAngle(arcs[i], arcs[j]); if (a > thr) bad[i] = true; }
            if (!isClosed)
            {
                int i = 0;
                while (i < n - 1)
                {
                    if (!bad[i]) { i++; continue; }
                    int k0 = i; int k1 = i; while (k1 + 1 < n - 1 && bad[k1 + 1]) k1++;
                    int a0 = k0; int a1 = k1 + 1; clusters.Add(Tuple.Create(a0, a1));
                    for (int k = k0; k <= k1; k++) debug_trigger_pts.Add(arcs[k].EndPoint);
                    Curve joined = JoinArcRange(arcs, a0, a1);
                    if (joined != null && joined.IsValid)
                    {
                        Point3d pS = joined.PointAtStart; Point3d pE = joined.PointAtEnd; Point3d pM = PointAtCurveMidByLength(joined); double tS, tM, tE;
                        if (rawCrv.ClosestPoint(pS, out tS) && rawCrv.ClosestPoint(pM, out tM) && rawCrv.ClosestPoint(pE, out tE))
                        {
                            if (!IsBetween(tM, tS, tE)) { double tmp = tS; tS = tE; tE = tmp; }
                            if (IsBetween(tM, tS, tE)) { Curve rawSeg = rawCrv.Trim(tS, tE); if (rawSeg != null && rawSeg.IsValid) debug_raw_segs.Add(rawSeg); }
                        }
                    }
                    i = k1 + 1;
                }
            }
            else
            {
                bool[] vis = new bool[n];
                for (int seed = 0; seed < n; seed++)
                {
                    if (!bad[seed] || vis[seed]) continue;
                    int k0 = seed; while (true) { int prev = (k0 - 1 + n) % n; if (!bad[prev] || vis[prev]) break; k0 = prev; if (k0 == seed) break; }
                    int k1 = seed; while (true) { int next = (k1 + 1) % n; if (!bad[next] || vis[next]) break; k1 = next; if (k1 == seed) break; }
                    int t = k0; while (true) { vis[t] = true; debug_trigger_pts.Add(arcs[t].EndPoint); if (t == k1) break; t = (t + 1) % n; }
                    int startArc = k0; int endArc = (k1 + 1) % n; clusters.Add(Tuple.Create(startArc, endArc));
                    List<int> idx = new List<int>(); int a = startArc; idx.Add(a); while (a != endArc) { a = (a + 1) % n; idx.Add(a); if (idx.Count > n + 2) break; }
                    Curve joined = JoinArcIndexList(arcs, idx);
                    if (joined != null && joined.IsValid)
                    {
                        Point3d pS = joined.PointAtStart; Point3d pE = joined.PointAtEnd; Point3d pM = PointAtCurveMidByLength(joined); double tS, tM, tE;
                        if (rawCrv.ClosestPoint(pS, out tS) && rawCrv.ClosestPoint(pM, out tM) && rawCrv.ClosestPoint(pE, out tE))
                        {
                            Curve rawSeg = ExtractClosedSegmentByMid(rawCrv, tS, tE, tM);
                            if (rawSeg != null && rawSeg.IsValid) debug_raw_segs.Add(rawSeg);
                        }
                    }
                }
            }
        }

        private void ApplyKinkClustersRebuild(Curve rawCrv, ref List<Arc> arcs, List<Tuple<int, int>> clusters, double tol, double angTolRad, double angTolDeg, double maxL, double minL, int iterIndex, int failBudget, int resolution, bool midpoint_optimize, bool dir_optimize, bool optimize_shorts, bool isClosed, out int replacedCount, out double usedMaxL_local, out double usedMinL_local)
        {
            replacedCount = 0; usedMaxL_local = maxL; usedMinL_local = minL;
            List<double> triedMaxL2 = new List<double>(); double minTriedMaxL2 = double.PositiveInfinity; double minChosenMaxL2 = double.PositiveInfinity; double chosenMaxL2 = double.NaN;
            if (rawCrv == null || arcs == null || arcs.Count < 2) return;
            if (clusters == null || clusters.Count == 0) return;

            double baseMinScale = 0.7; if (iterIndex >= 2) baseMinScale = 0.5; else if (iterIndex == 1) baseMinScale = 0.6;
            double minScale = baseMinScale - 0.05 * failBudget; if (minScale < 0.25) minScale = 0.25;
            double localMinLen = Math.Max(0.1, minL * minScale);
            usedMinL_local = localMinLen;
            double maxLenBaseOverall = 0.0;
            List<double> factorList = new List<double>() { 1.00, 0.85 };
            if (failBudget >= 1) factorList.Add(0.70); if (failBudget >= 2) factorList.Add(0.55); if (failBudget >= 3) factorList.Add(0.45);
            if (failBudget >= 4) factorList.Add(0.35); if (failBudget >= 5) factorList.Add(0.28); if (failBudget >= 6) factorList.Add(0.22);
            if (iterIndex >= 2) { if (factorList.Count > 0 && Math.Abs(factorList[0] - 1.00) < 1e-9) factorList.RemoveAt(0); }
            double[] factors = factorList.ToArray();

            if (!isClosed)
            {
                List<Tuple<int, int>> sorted = new List<Tuple<int, int>>(clusters); sorted.Sort(CompareTupleStartDesc);
                foreach (var cl in sorted)
                {
                    int a0 = cl.Item1; int a1 = cl.Item2; if (a1 <= a0) continue; if (a0 < 0 || a1 >= arcs.Count) continue;
                    bool hasLeft = (a0 - 1) >= 0; bool hasRight = (a1 + 1) < arcs.Count;
                    double beforeLeft = hasLeft ? GetTangentAngle(arcs[a0 - 1], arcs[a0]) : 0.0; double beforeRight = hasRight ? GetTangentAngle(arcs[a1], arcs[a1 + 1]) : 0.0;
                    Curve joined = JoinArcRange(arcs, a0, a1); if (joined == null || !joined.IsValid) continue;
                    Point3d pS = joined.PointAtStart; Point3d pE = joined.PointAtEnd; Point3d pM = PointAtCurveMidByLength(joined);
                    double tS, tM, tE; if (!rawCrv.ClosestPoint(pS, out tS) || !rawCrv.ClosestPoint(pM, out tM) || !rawCrv.ClosestPoint(pE, out tE)) continue;
                    if (!IsBetween(tM, tS, tE)) { double tmp = tS; tS = tE; tE = tmp; }
                    if (!IsBetween(tM, tS, tE)) continue;
                    Curve rawSeg = rawCrv.Trim(tS, tE); if (rawSeg == null || !rawSeg.IsValid) continue;
                    Curve work = rawSeg.DuplicateCurve(); double segLen = work.GetLength(); if (segLen <= 1e-9) continue; work.Domain = new Interval(0, segLen);
                    int beforeKinks; double beforeMaxAng; GetKinkStatsForArcRange(arcs, a0, a1, out beforeKinks, out beforeMaxAng);
                    List<Arc> bestNew = null;
                    double LmaxOld = 0.0; for (int kk = a0; kk <= a1; kk++) { if (kk < 0 || kk >= arcs.Count) continue; double al = arcs[kk].Length; if (al > LmaxOld) LmaxOld = al; }
                    double maxLenBase = 1.2 * LmaxOld; if (maxLenBase < minL * 1.2) maxLenBase = minL * 1.2; if (maxLenBase > maxLenBaseOverall) maxLenBaseOverall = maxLenBase;
                    double angImproveEpsDeg = 0.05;

                    for (int fi = 0; fi < factors.Length; fi++)
                    {
                        double factorLocal = factors[fi]; double maxL2 = maxLenBase * factorLocal; if (maxL2 > maxL) maxL2 = maxL;
                        double kGuard = 3.0 - 0.2 * failBudget; if (kGuard < 1.8) kGuard = 1.8;
                        double minAllowedMax = kGuard * localMinLen; if (maxL2 < minAllowedMax) maxL2 = minAllowedMax;
                        if (maxL2 > maxLenBase) maxL2 = maxLenBase;
                        triedMaxL2.Add(maxL2); if (maxL2 < minTriedMaxL2) minTriedMaxL2 = maxL2;

                        List<Arc> cand = SolveOnWorkWithDir(work, tol, angTolRad, maxL2, localMinLen, resolution, midpoint_optimize, dir_optimize);
                        if (cand == null || cand.Count == 0) continue;
                        if (optimize_shorts) { cand = LightShortsOptimize_LocalOnly(work, cand, localMinLen, maxL2, tol, midpoint_optimize); if (cand == null || cand.Count == 0) continue; }
                        if (!CheckMaxDevPass(work, cand, tol)) continue;

                        int afterKinks; double afterMaxAng; GetKinkStatsForLocalArcs(cand, out afterKinks, out afterMaxAng);
                        if (afterKinks > beforeKinks) continue;
                        if (!AnglePass(beforeMaxAng, afterMaxAng, angTolDeg)) continue;
                        if (hasLeft) { double afterLeft = GetTangentAngle(arcs[a0 - 1], cand[0]); if (!AnglePass(beforeLeft, afterLeft, angTolDeg)) continue; }
                        if (hasRight)
                        {
                            double afterRight = GetTangentAngle(cand[cand.Count - 1], arcs[a1 + 1]);
                            if (afterKinks == beforeKinks && afterMaxAng >= beforeMaxAng - angImproveEpsDeg) continue;
                            if (!AnglePass(beforeRight, afterRight, angTolDeg)) continue;
                        }
                        chosenMaxL2 = maxL2; if (maxL2 < minChosenMaxL2) minChosenMaxL2 = maxL2; bestNew = cand; break;
                    }
                    if (bestNew == null) continue;
                    arcs.RemoveRange(a0, a1 - a0 + 1); arcs.InsertRange(a0, bestNew); replacedCount++;
                }
            }
            else
            {
                foreach (var cl in clusters)
                {
                    int n = arcs.Count; if (n < 2) return;
                    int startArc = ((cl.Item1 % n) + n) % n; int endArc = ((cl.Item2 % n) + n) % n;
                    List<int> idx = new List<int>(); int a = startArc; idx.Add(a); while (a != endArc) { a = (a + 1) % n; idx.Add(a); if (idx.Count > n + 2) break; }
                    if (idx.Count < 2 || idx.Count > n) continue; HashSet<int> idxSet = new HashSet<int>(idx);
                    Curve joined = JoinArcIndexList(arcs, idx); if (joined == null || !joined.IsValid) continue;
                    Point3d pS = joined.PointAtStart; Point3d pE = joined.PointAtEnd; Point3d pM = PointAtCurveMidByLength(joined);
                    double tS, tM, tE; if (!rawCrv.ClosestPoint(pS, out tS) || !rawCrv.ClosestPoint(pM, out tM) || !rawCrv.ClosestPoint(pE, out tE)) continue;
                    Curve rawSeg = ExtractClosedSegmentByMid(rawCrv, tS, tE, tM); if (rawSeg == null || !rawSeg.IsValid) continue;
                    Curve work = rawSeg.DuplicateCurve(); double segLen = work.GetLength(); if (segLen <= 1e-9) continue; work.Domain = new Interval(0, segLen);
                    int leftIdx = PrevIndexNotInSet(idxSet, startArc, n); int rightIdx = NextIndexNotInSet(idxSet, endArc, n);
                    bool hasLeft = leftIdx >= 0; bool hasRight = rightIdx >= 0;
                    double beforeLeft = hasLeft ? GetTangentAngle(arcs[leftIdx], arcs[startArc]) : 0.0; double beforeRight = hasRight ? GetTangentAngle(arcs[endArc], arcs[rightIdx]) : 0.0;
                    int beforeKinks; double beforeMaxAng; GetKinkStatsForIndexSet(arcs, idxSet, out beforeKinks, out beforeMaxAng);
                    double LmaxOld_c = 0.0; foreach (int kk in idxSet) { if (kk < 0 || kk >= arcs.Count) continue; double al = arcs[kk].Length; if (al > LmaxOld_c) LmaxOld_c = al; }
                    double maxLenBase_c = 1.2 * LmaxOld_c; if (maxLenBase_c < minL * 1.2) maxLenBase_c = minL * 1.2; if (maxLenBase_c > maxLenBaseOverall) maxLenBaseOverall = maxLenBase_c;
                    List<Arc> bestNew = null;

                    for (int fi = 0; fi < factors.Length; fi++)
                    {
                        double factorLocal = factors[fi]; double maxL2 = maxLenBase_c * factorLocal; if (maxL2 > maxL) maxL2 = maxL;
                        double kGuard = 3.0 - 0.2 * failBudget; if (kGuard < 1.8) kGuard = 1.8;
                        double minAllowedMax = kGuard * localMinLen; if (maxL2 < minAllowedMax) maxL2 = minAllowedMax;
                        if (maxL2 > maxLenBase_c) maxL2 = maxLenBase_c;
                        triedMaxL2.Add(maxL2); if (maxL2 < minTriedMaxL2) minTriedMaxL2 = maxL2;

                        List<Arc> cand = SolveOnWorkWithDir(work, tol, angTolRad, maxL2, localMinLen, resolution, midpoint_optimize, dir_optimize);
                        if (cand == null || cand.Count == 0) continue;
                        if (optimize_shorts) { cand = LightShortsOptimize_LocalOnly(work, cand, localMinLen, maxL2, tol, midpoint_optimize); if (cand == null || cand.Count == 0) continue; }
                        if (!CheckMaxDevPass(work, cand, tol)) continue;
                        int afterKinks; double afterMaxAng; GetKinkStatsForLocalArcs(cand, out afterKinks, out afterMaxAng);
                        if (afterKinks > beforeKinks) continue;
                        if (!AnglePass(beforeMaxAng, afterMaxAng, angTolDeg)) continue;
                        if (hasLeft) { double afterLeft = GetTangentAngle(arcs[leftIdx], cand[0]); if (!AnglePass(beforeLeft, afterLeft, angTolDeg)) continue; }
                        if (hasRight) { double afterRight = GetTangentAngle(cand[cand.Count - 1], arcs[rightIdx]); if (!AnglePass(beforeRight, afterRight, angTolDeg)) continue; }
                        chosenMaxL2 = maxL2; if (maxL2 < minChosenMaxL2) minChosenMaxL2 = maxL2; bestNew = cand; break;
                    }
                    if (bestNew == null) continue;
                    List<Arc> rebuilt = new List<Arc>(arcs.Count - idx.Count + bestNew.Count); bool inserted = false;
                    for (int k = 0; k < arcs.Count; k++) { if (idxSet.Contains(k)) { if (!inserted) { rebuilt.AddRange(bestNew); inserted = true; } continue; } rebuilt.Add(arcs[k]); }
                    arcs = rebuilt; replacedCount++;
                }
            }
            if (!double.IsInfinity(minChosenMaxL2)) usedMaxL_local = minChosenMaxL2; else if (!double.IsInfinity(minTriedMaxL2)) usedMaxL_local = minTriedMaxL2;
            if (double.IsNaN(chosenMaxL2) && triedMaxL2.Count == 0 && maxLenBaseOverall > 0.0) usedMaxL_local = maxLenBaseOverall;
        }

        private Curve ExtractClosedSegmentByMid(Curve crv, double tS, double tE, double tM)
        {
            Interval dom = crv.Domain; tS = Math.Max(dom.Min, Math.Min(dom.Max, tS)); tE = Math.Max(dom.Min, Math.Min(dom.Max, tE)); tM = Math.Max(dom.Min, Math.Min(dom.Max, tM));
            if (Math.Abs(tS - tE) < 1e-7) return null; double a = Math.Min(tS, tE); double b = Math.Max(tS, tE);
            Curve[] sp = crv.Split(new double[] { a, b }); if (sp == null || sp.Length < 2) return null;
            Point3d pm = crv.PointAt(tM); Curve best = null; double bestDist = double.MaxValue;
            foreach (Curve seg in sp) { if (seg == null || !seg.IsValid) continue; double tt; if (!seg.ClosestPoint(pm, out tt)) continue; double d = seg.PointAt(tt).DistanceTo(pm); if (d < bestDist) { bestDist = d; best = seg; } }
            if (best == null || bestDist > 1e-3) return null; return best;
        }

        private List<Arc> SolveOnWorkWithDir(Curve work, double tol, double angTolRad, double maxL2, double minL2, int resolution, bool midpoint_optimize, bool dir_optimize)
        {
            List<double> ds, ts; List<Line> ls;
            List<Arc> best = SolveMethodLimitedForceClose(work, tol, angTolRad, maxL2, minL2, resolution, midpoint_optimize, out ds, out ls, out ts);
            if (!dir_optimize) return best;
            Curve rev = work.DuplicateCurve(); rev.Reverse(); rev.Domain = work.Domain;
            List<double> dsR, tsR; List<Line> lsR;
            List<Arc> candR = SolveMethodLimitedForceClose(rev, tol, angTolRad, maxL2, minL2, resolution, midpoint_optimize, out dsR, out lsR, out tsR);
            if (candR == null) return best; if (best == null) return FlipArcsToForward(candR);
            double avgA = (ds != null && ds.Count > 0) ? ds.Average() : 1e100; double avgB = (dsR != null && dsR.Count > 0) ? dsR.Average() : 1e100;
            if (candR.Count < best.Count || (candR.Count == best.Count && avgB < avgA)) return FlipArcsToForward(candR);
            return best;
        }

        private List<Arc> FlipArcsToForward(List<Arc> arcsRev)
        {
            if (arcsRev == null) return null; List<Arc> outA = new List<Arc>(arcsRev.Count);
            for (int i = arcsRev.Count - 1; i >= 0; i--) { Arc a = arcsRev[i]; a.Reverse(); outA.Add(a); }
            return outA;
        }

        private bool CheckMaxDevPass(Curve work, List<Arc> arcsLocal, double tol)
        {
            if (work == null || arcsLocal == null || arcsLocal.Count == 0) return false;
            List<double> ts = new List<double>(); double t0; work.ClosestPoint(arcsLocal[0].StartPoint, out t0); ts.Add(t0);
            for (int i = 0; i < arcsLocal.Count; i++) { double t; work.ClosestPoint(arcsLocal[i].EndPoint, out t); ts.Add(t); }
            for (int i = 0; i < arcsLocal.Count; i++) { Line l; double d = GetMaxDev_Fast(work, arcsLocal[i], ts[i], ts[i + 1], tol + 1e-6, out l); if (d > tol + 1e-6) return false; }
            return true;
        }

        private void GetKinkStatsForLocalArcs(List<Arc> arcsLocal, out int kinkCount, out double maxAngle)
        {
            kinkCount = 0; maxAngle = 0.0; if (arcsLocal == null || arcsLocal.Count < 2) return;
            for (int i = 0; i < arcsLocal.Count - 1; i++) { double a = GetTangentAngle(arcsLocal[i], arcsLocal[i + 1]); if (a > maxAngle) maxAngle = a; }
            double band = maxAngle - 1e-9; for (int i = 0; i < arcsLocal.Count - 1; i++) { double a = GetTangentAngle(arcsLocal[i], arcsLocal[i + 1]); if (a >= band) kinkCount++; }
        }

        private void GetKinkStatsForArcRange(List<Arc> arcsAll, int a0, int a1, out int kinkCount, out double maxAngle)
        {
            kinkCount = 0; maxAngle = 0.0; if (arcsAll == null || arcsAll.Count < 2) return; if (a0 < 0) a0 = 0; if (a1 >= arcsAll.Count) a1 = arcsAll.Count - 1; if (a1 - a0 < 1) return;
            for (int i = a0; i < a1; i++) { double a = GetTangentAngle(arcsAll[i], arcsAll[i + 1]); if (a > maxAngle) maxAngle = a; }
            double band = maxAngle - 1e-9; for (int i = a0; i < a1; i++) { double a = GetTangentAngle(arcsAll[i], arcsAll[i + 1]); if (a >= band) kinkCount++; }
        }

        private void GetKinkStatsForIndexSet(List<Arc> arcsAll, HashSet<int> idxSet, out int kinkCount, out double maxAngle)
        {
            kinkCount = 0; maxAngle = 0.0; if (arcsAll == null || arcsAll.Count < 2 || idxSet == null) return; List<int> idx = new List<int>(idxSet); idx.Sort();
            for (int ii = 0; ii < idx.Count - 1; ii++) { int i = idx[ii]; int j = idx[ii + 1]; if (j == i + 1) { double a = GetTangentAngle(arcsAll[i], arcsAll[j]); if (a > maxAngle) maxAngle = a; } }
            int last = idx[idx.Count - 1]; int first = idx[0]; if ((last + 1) % arcsAll.Count == first) { double a = GetTangentAngle(arcsAll[last], arcsAll[first]); if (a > maxAngle) maxAngle = a; }
            double band = maxAngle - 1e-9; for (int ii = 0; ii < idx.Count - 1; ii++) { int i = idx[ii]; int j = idx[ii + 1]; if (j == i + 1) { double a = GetTangentAngle(arcsAll[i], arcsAll[j]); if (a >= band) kinkCount++; } }
            if ((last + 1) % arcsAll.Count == first) { double a = GetTangentAngle(arcsAll[last], arcsAll[first]); if (a >= band) kinkCount++; }
        }

        private List<Arc> LightShortsOptimize_LocalOnly(Curve work, List<Arc> arcsLocal, double localMinLen, double maxL2, double tol, bool midpoint_optimize)
        {
            if (work == null || arcsLocal == null || arcsLocal.Count < 2) return arcsLocal;
            List<double> ts = new List<double>(); double t0; work.ClosestPoint(arcsLocal[0].StartPoint, out t0); ts.Add(t0);
            for (int i = 0; i < arcsLocal.Count; i++) { double t; work.ClosestPoint(arcsLocal[i].EndPoint, out t); ts.Add(t); }
            int passes = 2;
            for (int p = 0; p < passes; p++)
            {
                bool changed = false;
                for (int i = 0; i < arcsLocal.Count; i++)
                {
                    if (arcsLocal[i].Length >= localMinLen) continue;
                    if (i > 0) { double d; Line l; Arc merged = tryFitRaw(work, ts[i - 1], ts[i + 1], 1e10, midpoint_optimize, out d, out l); if (merged.IsValid && merged.Length <= maxL2 + 1e-3) { arcsLocal[i - 1] = merged; arcsLocal.RemoveAt(i); ts.RemoveAt(i); changed = true; break; } }
                    if (i < arcsLocal.Count - 1) { double d; Line l; Arc merged = tryFitRaw(work, ts[i], ts[i + 2], 1e10, midpoint_optimize, out d, out l); if (merged.IsValid && merged.Length <= maxL2 + 1e-3) { arcsLocal[i] = merged; arcsLocal.RemoveAt(i + 1); ts.RemoveAt(i + 1); changed = true; break; } }
                }
                if (!changed) break; ts.Clear(); work.ClosestPoint(arcsLocal[0].StartPoint, out t0); ts.Add(t0); for (int i = 0; i < arcsLocal.Count; i++) { double t; work.ClosestPoint(arcsLocal[i].EndPoint, out t); ts.Add(t); }
            }
            return arcsLocal;
        }

        private int PrevIndexNotInSet(HashSet<int> set, int start, int n) { if (set == null) return -1; int k = start; for (int i = 0; i < n; i++) { k = (k - 1 + n) % n; if (!set.Contains(k)) return k; } return -1; }
        private int NextIndexNotInSet(HashSet<int> set, int start, int n) { if (set == null) return -1; int k = start; for (int i = 0; i < n; i++) { k = (k + 1) % n; if (!set.Contains(k)) return k; } return -1; }
        private static int CompareTupleStartDesc(Tuple<int, int> A, Tuple<int, int> B) { if (A == null && B == null) return 0; if (A == null) return 1; if (B == null) return -1; return B.Item1.CompareTo(A.Item1); }
        private bool AnglePass(double beforeDeg, double afterDeg, double angTolDeg) { double thr = angTolDeg + 0.05; if (beforeDeg <= thr) return afterDeg <= thr; return afterDeg <= beforeDeg + 0.05; }

        private bool TryClosestPointOnArc(Arc a, Point3d pt, out Point3d pa)
        {
            pa = Point3d.Unset;
            if (!a.IsValid) return false;
            Plane pl = a.Plane;
            double u, v; pl.ClosestParameter(pt, out u, out v); Point3d pProj = pl.PointAt(u, v);
            Vector3d r = pProj - a.Center;
            if (r.Length < 1e-10) { pa = a.StartPoint; return true; }
            r.Unitize();
            Point3d pOnCircle = a.Center + r * a.Radius;
            double angStart = ParamAngleOnPlane(pl, a.StartPoint - a.Center);
            double angEnd = ParamAngleOnPlane(pl, a.EndPoint - a.Center);
            double angCand = ParamAngleOnPlane(pl, pOnCircle - a.Center);
            Point3d mid = a.PointAt(a.StartAngle + a.Angle * 0.5); // Fixed
            double angMid = ParamAngleOnPlane(pl, mid - a.Center);
            bool midInCCW = AngleInCCWRange(angStart, angEnd, angMid);
            bool candInCCW = AngleInCCWRange(angStart, angEnd, angCand);
            if (midInCCW ? candInCCW : !candInCCW) { pa = pOnCircle; return true; }
            Point3d p0 = a.StartPoint; Point3d p1 = a.EndPoint;
            pa = (pt.DistanceTo(p0) <= pt.DistanceTo(p1)) ? p0 : p1;
            return true;
        }

        private double ParamAngleOnPlane(Plane pl, Vector3d vecFromCenter) { double x = vecFromCenter * pl.XAxis; double y = vecFromCenter * pl.YAxis; double ang = Math.Atan2(y, x); return NormalizeAngle2Pi(ang); }
        private double NormalizeAngle2Pi(double a) { double twoPi = 2.0 * Math.PI; a = a % twoPi; if (a < 0) a += twoPi; return a; }
        private bool AngleInCCWRange(double start, double end, double ang) { if (end >= start) return ang >= start && ang <= end; return ang >= start || ang <= end; }

        public override Guid ComponentGuid => new Guid("63849502-1774-4861-8274-123456789000"); // 你的专属新GUID

        protected override System.Drawing.Bitmap Icon => Dung_Beetle.Properties.Resources.Crvtoarcs_icon1;

    }
}