using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Sliding_Simulation
{
    public class Sliding_SimulationComponent : GH_Component
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public Sliding_SimulationComponent()
          : base("Sliding_SimulationComponent", "滑行物理模拟 v61.2",
              "基于物理碰撞的滑行模拟器",
              "Dung Beetle",
              "Physics")
        {
        }

        /// <summary>
        /// 1. 定义输入端 (保姆级说明版)
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Obstacles", "Obs",
                "碰撞障碍物 (Brep/Mesh)\n" +
                "-----------------------\n" +
                "定义粒子运动的物理环境。\n" +
                "建议输入：地形曲面、滑梯模型、石笼墙容器、地面等。\n" +
                "注意：如果是复杂的组合体，请尽量炸开或确保法线方向正确。",
                GH_ParamAccess.list);

            pManager.AddPointParameter("StartPts", "Pts",
                "粒子发射点 (Point)\n" +
                "-----------------------\n" +
                "粒子的出生位置。\n" +
                "如果开启了 'AutoDrop'，这个点可以是空中的点，小球会自动投影到下方最近的障碍物上。",
                GH_ParamAccess.list);

            pManager.AddVectorParameter("InitVel", "Vel",
                "初始速度向量 (Vector)\n" +
                "-----------------------\n" +
                "定义粒子发射时的初速度方向和大小。\n" +
                "• 向量长度 = 速度大小 (单位: 米/秒)\n" +
                "• 向量方向 = 发射方向\n" ,
                GH_ParamAccess.list);

            pManager.AddNumberParameter("Radius", "R",
                "粒子半径 (Number)\n" +
                "-----------------------\n" +
                "定义小球的物理碰撞体积大小。\n" +
                "单位：与Rhino当前模型单位一致 (如mm或m)。\n" +
                "用于计算碰撞检测和堆积间隙。",
                GH_ParamAccess.list);

            pManager.AddNumberParameter("Friction", "F",
                "综合阻尼/摩擦系数 (Number 0.0~1.0)\n" +
                "-----------------------\n" +
                "定义接触面的综合阻力（已代偿弯道离心挤压、人体形变吸能与风阻）。\n" +
                "🔥 落地验证工程数据 (不锈钢滑梯)：\n\n" +
                "🎯 0.45 : 真实工况预测 (👉 默认/强烈推荐)\n" +
                "   • 基于线下 Benchmark 实测校准。\n" +
                "   • 用途：精确评估滑行总耗时、平均速度以及是否会在平缓段停滞。\n\n" +
                "🛡️ 0.35 : 常规安全校核 (保守审查)\n" +
                "   • 模拟穿着常规衣物且表面极其光滑的理想状态。\n" +
                "   • 用途：日常设计的安全底线检查。\n\n" +
                "🚨 0.30 : 极限工况校核 (防飞出/超载)\n" +
                "   • 模拟极低阻力状态（如穿着特氟龙丝滑运动裤或滑梯结露有水）。\n" +
                "   • 用途：专用于排查最高冲刺速度，验证弯道是否会飞出，以及最大 G 值（过载）是否超标风险。",
                GH_ParamAccess.list,
                0.45); // 默认值修改为团队实测基准数据

            pManager.AddNumberParameter("Restitution", "E",
                "弹性/反弹系数 (Number 0.0~1.0)\n" +
                "-----------------------\n" +
                "模拟小球的材质弹性。\n" +
                "• 0.0 = 湿泥巴 (完全吸能，不反弹)\n" +
                "• 0.9 = 超级弹力球 (几乎无损反弹)\n" +
                "推荐值：0.5~0.7 (模拟石头或硬塑料)",
                GH_ParamAccess.list,
                0.5);

            pManager.AddNumberParameter("Delay", "D",
                "激活延迟时间表 (Seconds)\n" +
                "-----------------------\n" +
                "控制每个粒子进入物理世界的时间。\n" +
                "单位：秒。\n" +
                "例如输入 [0, 0.5, 1.0]，表示第1个球立即落下，第2个球0.5秒后落下...",
                GH_ParamAccess.list);

            pManager.AddBooleanParameter("AutoDrop", "Drop",
                "自动贴地 (Boolean)\n" +
                "-----------------------\n" +
                "• True (推荐)：忽略起点高度，将小球直接吸附到下方最近的曲面上开始运动。\n" +
                "• False：从起点位置开始自由落体。",
                GH_ParamAccess.item, true);

            pManager.AddBooleanParameter("CollideParticles", "Col",
                "启用粒子互撞 (Boolean)\n" +
                "-----------------------\n" +
                "• True (Billiards模式)：小球之间会发生体积碰撞(适合堆积/填充)。\n" +
                "• False (Legacy模式)：小球互相穿透，只与地面碰撞(适合轨迹分析，速度极快)。",
                GH_ParamAccess.item, true);

            pManager.AddNumberParameter("Duration", "T",
                "模拟总时长 (Seconds)\n" +
                "-----------------------\n" +
                "物理模拟计算的总时间跨度。\n" +
                "如果不确定，可以设大一点 (例如 10~30秒)，确保小球能滚到底。",
                GH_ParamAccess.item, 10.0);

            pManager.AddNumberParameter("TimeStep", "dt",
                "时间步长/精度 (Seconds)\n" +
                "-----------------------\n" +
                "每一帧物理计算的时间间隔。\n" +
                "• 值越小 = 模拟越精确，不穿模，但计算慢。\n" +
                "• 值越大 = 计算快，但可能出现穿模。\n" +
                "推荐值：0.01 ~ 0.02。",
                GH_ParamAccess.item, 0.02);

            // 设置可选参数
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        /// <summary>
        /// 2. 定义输出端 (新手引导版)
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Message", "msg",
                "中文诊断报告 (Text)\n" +
                "-----------------------\n" +
                "包含物理模拟的完整统计数据，用于评估结果：\n" +
                "• 📊 粒子状态统计 (等待/运动/静止)\n" +
                "• 🏁 总里程与耗时\n" +
                "• 🚀 全局最高速度 (m/s)\n" +
                "• 💥 最大过载/冲击力 (G)\n" +
                "• 🔋 能量守恒审计 (用于检查模拟是否准确)",
                GH_ParamAccess.item);

            pManager.AddTransformParameter("Xforms", "X",
                "运动变换矩阵 (Transform Tree)\n" +
                "-----------------------\n" +
                "这是制作动画的核心数据。\n" +
                "包含每一帧粒子相对于世界坐标的位置与旋转。\n\n" +
                "💡 如何使用：\n" +
                "请配合原生 [Transform] 电池，将原始小球几何体移动到每一帧的位置，即可看到动画效果。",
                GH_ParamAccess.tree);

            pManager.AddCurveParameter("Traj", "Traj",
                "运动轨迹 (Curve Tree)\n" +
                "-----------------------\n" +
                "粒子划过的完整路径曲线 (Polyline)。\n\n" +
                "💡 用途：\n" +
                "• 检查滑行路线是否会冲出滑道\n" +
                "• 制作路径管道 (Pipe) 可视化\n" +
                "• 提取控制点进行后续分析",
                GH_ParamAccess.tree);

            pManager.AddVectorParameter("Vectors", "V",
                "速度向量 (Vector Tree)\n" +
                "-----------------------\n" +
                "记录每一帧的瞬时速度向量。\n\n" +
                "📝 说明：\n" +
                "• 向量方向 = 运动朝向\n" +
                "• 向量长度 = 速度大小 (单位已统一为 m/s)\n" +
                "可用于绘制速度箭头图示 (Vector Display)。",
                GH_ParamAccess.tree);

            pManager.AddNumberParameter("Speeds", "S",
                "瞬时速率 (Number Tree)\n" +
                "-----------------------\n" +
                "记录每一帧的速度大小 (标量)。\n" +
                "单位：米/秒 (m/s)。\n\n" +
                "💡 高级用法：\n" +
                "配合 Gradient (渐变) 电池，可以将轨迹按速度快慢进行着色分析（例如红色表示高速，绿色表示低速）。",
                GH_ParamAccess.tree);

            pManager.AddNumberParameter("G_Forces", "G",
                "过载/冲击力 (Number Tree)\n" +
                "-----------------------\n" +
                "记录每一帧的加速度过载 (G-Force)。\n\n" +
                "📝 数值参考：\n" +
                "• 1.0 G : 正常重力 (静止或匀速)\n" +
                "• >1.0 G : 超重 (转弯挤压或落地撞击)\n" +
                "• <1.0 G : 失重 (抛跳或下坠)\n" +
                "用于评估滑行体验的舒适度和安全性。",
                GH_ParamAccess.tree);
        }

        /// <summary>
        /// 3. 核心计算逻辑
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Brep> Obstacles = new List<Brep>();
            List<Point3d> StartPts = new List<Point3d>();
            List<Vector3d> InitVel = new List<Vector3d>();
            List<double> Radius = new List<double>();
            List<double> Friction = new List<double>();
            List<double> Restitution = new List<double>();
            List<double> Delay = new List<double>();

            bool AutoDrop = false;
            bool CollideParticles = false;
            double Duration = 10.0;
            double TimeStep = 0.01;

            if (!DA.GetDataList(0, Obstacles)) return;
            if (!DA.GetDataList(1, StartPts)) return;

            DA.GetDataList(2, InitVel);
            DA.GetDataList(3, Radius);
            DA.GetDataList(4, Friction);
            DA.GetDataList(5, Restitution);
            DA.GetDataList(6, Delay);
            DA.GetData(7, ref AutoDrop);
            DA.GetData(8, ref CollideParticles);
            DA.GetData(9, ref Duration);
            DA.GetData(10, ref TimeStep);

            if (InitVel.Count == 0) InitVel.Add(Vector3d.Zero);
            if (Radius.Count == 0) Radius.Add(10.0);
            if (Friction.Count == 0) Friction.Add(0.35); // 确保内部逻辑也是 0.35
            if (Restitution.Count == 0) Restitution.Add(0.5);
            if (Delay.Count == 0) Delay.Add(0.0);

            if (Obstacles == null || Obstacles.Count == 0) return;
            if (StartPts == null || StartPts.Count == 0) return;

            int count = StartPts.Count;
            SimulationResult[] results = new SimulationResult[count];
            bool detailedLog = (count == 1);

            bool useEngineB = CollideParticles && (count > 1);

            if (!useEngineB)
            {
                Parallel.For(0, count, i =>
                {
                    Vector3d v0 = InitVel[i % InitVel.Count];
                    double r = Radius[i % Radius.Count];
                    double f = Friction[i % Friction.Count];
                    double d = Delay[i % Delay.Count];
                    results[i] = Engine_Legacy_v41_Strict(Obstacles, StartPts[i], v0, r, f, d, AutoDrop, Duration, TimeStep, i, detailedLog);
                });
            }
            else
            {
                results = Engine_Billiards_v55(count, Obstacles, StartPts, InitVel, Radius, Friction, Restitution, Delay, AutoDrop, Duration, TimeStep, detailedLog);
            }

            GH_Structure<GH_Matrix> outXformsStruct = new GH_Structure<GH_Matrix>();
            GH_Structure<GH_Curve> outTrajStruct = new GH_Structure<GH_Curve>();
            GH_Structure<GH_Vector> outVectorsStruct = new GH_Structure<GH_Vector>();
            GH_Structure<GH_Number> outSpeedsStruct = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> outGForcesStruct = new GH_Structure<GH_Number>();

            string finalMsg = "";

            for (int i = 0; i < count; i++)
            {
                GH_Path path = new GH_Path(i);
                var res = results[i];
                if (res == null) continue;

                if (res.Xforms != null)
                {
                    // ✅ 修复点：显式转换 Transform -> Matrix
                    List<GH_Matrix> xfs = res.Xforms.Select(x => new GH_Matrix(new Rhino.Geometry.Matrix(x))).ToList();
                    outXformsStruct.AppendRange(xfs, path);
                }
                if (res.Trajectory != null)
                {
                    outTrajStruct.Append(new GH_Curve(res.Trajectory), path);
                }
                if (res.Vectors != null)
                {
                    List<GH_Vector> vecs = res.Vectors.Select(v => new GH_Vector(v)).ToList();
                    outVectorsStruct.AppendRange(vecs, path);
                }
                if (res.Speeds != null)
                {
                    List<GH_Number> spds = res.Speeds.Select(s => new GH_Number(s)).ToList();
                    outSpeedsStruct.AppendRange(spds, path);
                }
                if (res.GForces != null)
                {
                    List<GH_Number> gs = res.GForces.Select(g => new GH_Number(g)).ToList();
                    outGForcesStruct.AppendRange(gs, path);
                }
            }

            if (count == 1)
                finalMsg = results[0].Log;
            else
                finalMsg = GenerateBatchReport_v56(results, Duration, TimeStep, Friction[0], useEngineB, count);

            DA.SetData(0, finalMsg);
            DA.SetDataTree(1, outXformsStruct);
            DA.SetDataTree(2, outTrajStruct);
            DA.SetDataTree(3, outVectorsStruct);
            DA.SetDataTree(4, outSpeedsStruct);
            DA.SetDataTree(5, outGForcesStruct);
        }

        // --- 4. 辅助方法与数据类 ---

        private string GenerateBatchReport_v56(SimulationResult[] results, double dur, double dt, double fric, bool engineB, int count)
        {
            StringBuilder sb = new StringBuilder();
            double maxSpeed = 0;
            int maxSpeedID = -1;
            double totalSystemKE = 0;
            int stoppedCount = 0;
            int activeCount = 0;
            int waitingCount = 0;
            double totalDist = 0;
            double lastSystemStopTime = 0;

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] == null) continue;
                if (!results[i].IsActive) waitingCount++;

                double s = results[i].CurrentVel.Length * 0.001;
                if (s > maxSpeed) { maxSpeed = s; maxSpeedID = i; }

                double ke = 0.5 * results[i].CurrentVel.SquareLength;
                totalSystemKE += ke;

                if (results[i].StopTime >= 0)
                {
                    stoppedCount++;
                    if (results[i].StopTime > lastSystemStopTime) lastSystemStopTime = results[i].StopTime;
                }
                else
                {
                    activeCount++;
                }
                totalDist += results[i].TotalDist;
            }

            sb.AppendLine("╔══════════════════════════════════╗");
            sb.AppendLine($"║   🎱  物理引擎指挥舱 v61.2       ║");
            sb.AppendLine("╚══════════════════════════════════╝");
            sb.AppendLine("");
            sb.AppendLine($"[ 全局参数 ]");
            sb.AppendLine($"• 粒子总数   : {count}");
            sb.AppendLine($"• 模拟时长   : {dur:F2} 秒");
            sb.AppendLine($"• 引擎模式   : {(engineB ? "🎱 刚体碰撞 (Billiards)" : "🚀 极速并行 (Legacy)")}");
            sb.AppendLine($"• 摩擦系数   : {fric:F4}");
            sb.AppendLine("");
            sb.AppendLine($"[ 舰队状态 ]");
            sb.AppendLine($"• ⏳ 等待中   : {waitingCount}");
            sb.AppendLine($"• 🟢 运动中   : {activeCount - waitingCount}");
            sb.AppendLine($"• 🛑 已静止   : {stoppedCount}");

            if (activeCount == 0 && waitingCount == 0 && count > 0)
            {
                sb.AppendLine($"• 🏁 全部停止 : {lastSystemStopTime:F2} 秒 (耗时)");
            }
            sb.AppendLine($"• 全局极速   : {maxSpeed:F2} m/s (ID: {maxSpeedID})");
            sb.AppendLine($"• 平均里程   : {(totalDist / count / 1000.0):F2} 米");
            sb.AppendLine("");
            sb.AppendLine($"[ 系统能量 ]");
            sb.AppendLine($"• 系统总动能 : {(totalSystemKE / 1000000.0):F4} (MJ/kg)");

            return sb.ToString();
        }

        public class SimulationResult
        {
            public string Log;
            public List<Transform> Xforms = new List<Transform>();
            public PolylineCurve Trajectory;
            public List<Vector3d> Vectors = new List<Vector3d>();
            public List<double> Speeds = new List<double>();
            public List<double> GForces = new List<double>();

            public Point3d CurrentPos;
            public Vector3d CurrentVel;
            public Vector3d LaunchVel;
            public double ActivationTime = 0;
            public bool IsActive = false;

            public double MaxSpeed = 0;
            public double MaxG = 0;
            public double StopTime = -1;
            public double TotalDist = 0;

            public double TotalME = 0;
            public double AccLoss = 0;
            public bool WasColliding = false;

            public Plane BasePlane;
        }

        private SimulationResult Engine_Legacy_v41_Strict(
          List<Brep> Obstacles, Point3d StartPt, Vector3d InitVel,
          double Radius, double Friction, double Delay,
          bool AutoDrop, double Duration, double TimeStep, int id, bool detailed)
        {
            SimulationResult res = new SimulationResult();
            res.ActivationTime = Delay;
            res.IsActive = (Delay <= 0);

            StringBuilder log = new StringBuilder();

            double gVal = 9810.0;
            Vector3d gravityVec = new Vector3d(0, 0, -gVal);
            int solverIterations = 5;
            double airDragCoeff = (Friction < 1e-6) ? 0.0 : 0.00002;
            Point3d pos = StartPt;

            Vector3d vel = InitVel * 1000.0;

            if (AutoDrop)
            {
                Ray3d dropRay = new Ray3d(StartPt, -Vector3d.ZAxis);
                Point3d[] hits = Intersection.RayShoot(dropRay, Obstacles.Select(b => (GeometryBase)b), 1);
                if (hits != null && hits.Length > 0)
                {
                    pos = hits[0] + Vector3d.ZAxis * (Radius * 1.05);
                    Vector3d dropVel = Vector3d.Zero;
                    for (int k = 0; k < 200; k++)
                    {
                        dropVel += gravityVec * TimeStep;
                        Point3d nextD = pos + dropVel * TimeStep;
                        SolvePositionAccumulated(Obstacles, ref nextD, Radius, 8);
                        dropVel *= 0.5; pos = nextD;
                        if (dropVel.Length < 0.1 && k > 20) break;
                    }

                    Point3d probeP = pos;
                    Vector3d surfN = SolvePositionAccumulated(Obstacles, ref probeP, Radius, 2);
                    if (surfN.Length > 0.001)
                    {
                        surfN.Unitize();
                        double targetSpeed = vel.Length;
                        if (targetSpeed > 0.001)
                        {
                            Vector3d proj = vel - surfN * (vel * surfN);
                            if (proj.Length > 0.001)
                            {
                                proj.Unitize();
                                vel = proj * targetSpeed;
                            }
                        }
                    }
                }
            }

            res.LaunchVel = vel;
            if (!res.IsActive) vel = Vector3d.Zero;

            int steps = (int)(Duration / TimeStep);

            double initialKE = 0.5 * res.LaunchVel.SquareLength;
            double initialPE = gVal * pos.Z;
            double totalMechanicalEnergy = initialKE + initialPE;
            double accumulatedLossWork = 0.0;

            Vector3d smoothVisualFwd = res.LaunchVel.Length > 0.1 ? res.LaunchVel : Vector3d.XAxis;
            Vector3d smoothVisualNormal = Vector3d.ZAxis;
            bool wasColliding = false;

            Vector3d fwd0 = res.LaunchVel.Length > 0.1 ? res.LaunchVel : Vector3d.XAxis;
            Plane basePlane = new Plane(StartPt, fwd0, Vector3d.ZAxis);

            Plane firstFramePlane = new Plane(pos, smoothVisualFwd, smoothVisualNormal);
            if (!firstFramePlane.IsValid) firstFramePlane = Plane.WorldXY;
            res.Xforms.Add(Transform.PlaneToPlane(basePlane, firstFramePlane));

            res.Speeds.Add(vel.Length * 0.001);
            res.Vectors.Add(vel * 0.001);
            res.GForces.Add(1.0);
            List<Point3d> pathPts = new List<Point3d> { pos };

            double maxSpeedRec = 0, maxGRec = 0, totalDistRec = 0;
            double stopTimeRecorded = -1.0;
            int hardResetCount = 0;

            for (int i = 0; i < steps; i++)
            {
                double currentTime = i * TimeStep;
                if (!res.IsActive)
                {
                    if (currentTime >= res.ActivationTime)
                    {
                        res.IsActive = true;
                        vel = res.LaunchVel;
                    }
                    else
                    {
                        if (res.Xforms.Count > 0) res.Xforms.Add(res.Xforms[res.Xforms.Count - 1]);
                        else res.Xforms.Add(Transform.Identity);

                        res.Speeds.Add(0.0);
                        res.Vectors.Add(Vector3d.Zero);
                        res.GForces.Add(0.0);
                        pathPts.Add(pos);
                        continue;
                    }
                }

                if (stopTimeRecorded >= 0)
                {
                    res.Xforms.Add(res.Xforms[res.Xforms.Count - 1]);
                    res.Speeds.Add(0.0);
                    res.Vectors.Add(Vector3d.Zero);
                    res.GForces.Add(0.0);
                    pathPts.Add(pos);
                    continue;
                }

                Vector3d oldVel = vel;
                Vector3d newtonianVel = vel + gravityVec * TimeStep;
                Point3d nextPos = pos + newtonianVel * TimeStep;
                Vector3d contactNormal = SolvePositionAccumulated(Obstacles, ref nextPos, Radius, solverIterations);
                bool isColliding = (contactNormal.Length > 0.001);
                if (isColliding) contactNormal.Unitize(); else contactNormal = Vector3d.ZAxis;
                bool isImpact = isColliding && !wasColliding;

                if (isColliding)
                {
                    double vProj = newtonianVel * contactNormal;
                    if (vProj < 0) newtonianVel -= contactNormal * vProj;
                }
                double stepDist = pos.DistanceTo(nextPos);
                totalDistRec += stepDist;

                if (isImpact)
                {
                    double currentKE = 0.5 * newtonianVel.SquareLength;
                    double currentPE = gVal * nextPos.Z;
                    totalMechanicalEnergy = currentKE + currentPE;
                    accumulatedLossWork = 0.0;
                }
                else if (isColliding)
                {
                    double frictionWork = (Math.Abs(gravityVec * contactNormal) * Friction) * stepDist;
                    double currentSpeed = vel.Length;
                    double dragWork = (airDragCoeff * currentSpeed * currentSpeed) * stepDist;
                    accumulatedLossWork += (frictionWork + dragWork);
                }
                else
                {
                    double currentSpeed = vel.Length;
                    double dragWork = (airDragCoeff * currentSpeed * currentSpeed) * stepDist;
                    accumulatedLossWork += dragWork;
                }

                double currentPE_Calc = gVal * nextPos.Z;
                double targetTotalKE = totalMechanicalEnergy - currentPE_Calc - accumulatedLossWork;
                if (targetTotalKE < 0) targetTotalKE = 0;
                double targetSpeed = Math.Sqrt(2.0 * targetTotalKE);

                if (isColliding && Friction < 1e-6)
                {
                    Vector3d slopeDir = gravityVec - contactNormal * (gravityVec * contactNormal);
                    double slopeAccel = slopeDir.Length;
                    if (slopeDir.Length > 1e-6) slopeDir.Unitize();
                    bool isMovingUp = (vel * slopeDir < 0);
                    double speedThreshold = slopeAccel * TimeStep * 2.0;

                    if (isMovingUp && targetSpeed < speedThreshold)
                    {
                        vel = Vector3d.Zero;
                        totalMechanicalEnergy = currentPE_Calc + accumulatedLossWork;
                        hardResetCount++;
                    }
                    else if (vel.Length < 1e-9 && slopeAccel > 1e-3)
                    {
                        vel = slopeDir * (slopeAccel * TimeStep);
                        totalMechanicalEnergy += 0.5 * vel.SquareLength;
                    }
                    else
                    {
                        Vector3d baseDir = newtonianVel;
                        if (baseDir.Length > 1e-6) baseDir.Unitize(); else baseDir = (vel.Length > 0.1) ? vel / vel.Length : Vector3d.XAxis;
                        Vector3d sideDir = Vector3d.CrossProduct(contactNormal, slopeDir);
                        if (sideDir.Length > 1e-6) sideDir.Unitize();
                        double vSide = newtonianVel * sideDir;
                        if (Math.Abs(vSide) < 0.5) { baseDir = baseDir - sideDir * (baseDir * sideDir); if (baseDir.Length > 1e-6) baseDir.Unitize(); }
                        vel = baseDir * targetSpeed;
                        double vSideFinal = vel * sideDir;
                        Vector3d vSlopePart = vel - sideDir * vSideFinal;
                        vel = vSlopePart + sideDir * (vSideFinal * 0.999);
                    }
                }
                else
                {
                    if (isColliding)
                    {
                        Vector3d slopeDir = gravityVec - contactNormal * (gravityVec * contactNormal);
                        if (slopeDir.Length > 1e-6) slopeDir.Unitize();
                        Vector3d sideDir = Vector3d.CrossProduct(contactNormal, slopeDir);
                        if (sideDir.Length > 1e-6) sideDir.Unitize();
                        Vector3d baseDir = newtonianVel;
                        if (baseDir.Length > 1e-6) baseDir.Unitize();
                        double vSide = newtonianVel * sideDir;
                        if (Math.Abs(vSide) < 0.5) baseDir = (baseDir - sideDir * (baseDir * sideDir));
                        if (baseDir.Length > 1e-6) baseDir.Unitize();
                        vel = baseDir * targetSpeed;
                        double slopeKE = 0.5 * Math.Pow(vel * slopeDir, 2);
                        Vector3d slopeG = gravityVec - contactNormal * (gravityVec * contactNormal);
                        if (Friction > 1e-6 && targetSpeed < 5.0 && slopeKE < 0.1)
                        {
                            if (slopeG.Length < gVal * Friction) vel = Vector3d.Zero;
                        }
                    }
                    else
                    {
                        vel = newtonianVel;
                        if (vel.Length > 0.001 && targetTotalKE > 0)
                        {
                            double airTargetSpeed = Math.Sqrt(2.0 * targetTotalKE);
                            double ratio = airTargetSpeed / vel.Length;
                            if (ratio < 0.95 || ratio > 1.05) { vel.Unitize(); vel *= airTargetSpeed; }
                        }
                    }
                }
                if (!nextPos.IsValid || double.IsNaN(nextPos.X)) break;
                wasColliding = isColliding;
                if (vel.Length < 1e-6 && isColliding) { if (stopTimeRecorded < 0) stopTimeRecorded = i * TimeStep; }
                else { stopTimeRecorded = -1.0; }
                if (vel.Length > maxSpeedRec) maxSpeedRec = vel.Length;
                Vector3d netAccel = (vel - oldVel) / TimeStep - gravityVec;
                double gForce = netAccel.Length / gVal;
                if (gForce > maxGRec) maxGRec = gForce;
                pos = nextPos;
                Vector3d targetVisualNormal = isColliding ? contactNormal : Vector3d.ZAxis;
                smoothVisualNormal = smoothVisualNormal * 0.9 + targetVisualNormal * 0.1;
                if (smoothVisualNormal.Length > 1e-6) smoothVisualNormal.Unitize(); else smoothVisualNormal = Vector3d.ZAxis;
                Vector3d targetFwd = vel;
                if (targetFwd.Length < 10.0) targetFwd = smoothVisualFwd;
                smoothVisualFwd = smoothVisualFwd * 0.9 + targetFwd * 0.1;
                if (smoothVisualFwd.Length > 1e-6) smoothVisualFwd.Unitize(); else smoothVisualFwd = Vector3d.XAxis;
                Plane currentPlane = new Plane(pos, smoothVisualFwd, smoothVisualNormal);
                if (!currentPlane.IsValid) currentPlane = Plane.WorldXY;
                res.Xforms.Add(Transform.PlaneToPlane(basePlane, currentPlane));
                res.Speeds.Add(vel.Length * 0.001);
                res.Vectors.Add(vel * 0.001);
                res.GForces.Add(gForce);
                pathPts.Add(pos);
            }

            if (detailed)
            {
                res.Log = log.ToString();
            }

            res.CurrentVel = vel;
            res.StopTime = stopTimeRecorded;
            res.TotalDist = totalDistRec;
            if (pathPts.Count > 1) res.Trajectory = new PolylineCurve(pathPts);
            return res;
        }

        private SimulationResult[] Engine_Billiards_v55(
          int count, List<Brep> Obstacles, List<Point3d> StartPts, List<Vector3d> InitVel,
          List<double> Radius, List<double> Friction, List<double> Restitution,
          List<double> Delay,
          bool AutoDrop, double Duration, double TimeStep, bool detailed)
        {
            SimulationResult[] results = new SimulationResult[count];
            double[] radiuses = new double[count];
            double[] masses = new double[count];
            double[] eValues = new double[count];
            double gVal = 9810.0;
            Vector3d gravityVec = new Vector3d(0, 0, -gVal);
            double airDragCoeff = 0.00002;

            for (int i = 0; i < count; i++)
            {
                results[i] = new SimulationResult();
                radiuses[i] = Radius[i % Radius.Count];
                masses[i] = Math.Pow(radiuses[i], 3);
                eValues[i] = Restitution[i % Restitution.Count];

                results[i].ActivationTime = Delay[i % Delay.Count];
                results[i].IsActive = (results[i].ActivationTime <= 0);

                Vector3d v0 = InitVel[i % InitVel.Count];
                Point3d p0 = StartPts[i];

                if (AutoDrop)
                {
                    Ray3d dropRay = new Ray3d(p0, -Vector3d.ZAxis);
                    Point3d[] hits = Intersection.RayShoot(dropRay, Obstacles.Select(b => (GeometryBase)b), 1);
                    if (hits != null && hits.Length > 0)
                    {
                        p0 = hits[0] + Vector3d.ZAxis * (radiuses[i] * 1.05);
                        Point3d probeP = p0;
                        Vector3d surfN = SolvePositionAccumulated(Obstacles, ref probeP, radiuses[i], 2);
                        if (surfN.Length > 0.001)
                        {
                            surfN.Unitize();
                            double speed0 = v0.Length;
                            if (speed0 > 0.001)
                            {
                                Vector3d proj = v0 - surfN * (v0 * surfN);
                                if (proj.Length > 0.001)
                                {
                                    proj.Unitize();
                                    v0 = proj * speed0;
                                }
                            }
                        }
                    }
                }
                results[i].CurrentPos = p0;
                results[i].LaunchVel = v0 * 1000.0;
                results[i].CurrentVel = results[i].IsActive ? results[i].LaunchVel : Vector3d.Zero;

                results[i].TotalME = 0.5 * results[i].LaunchVel.SquareLength + gVal * p0.Z;
                results[i].AccLoss = 0.0;
                results[i].WasColliding = false;

                Vector3d fwd0 = results[i].LaunchVel.Length > 0.1 ? results[i].LaunchVel : Vector3d.XAxis;

                results[i].BasePlane = new Plane(StartPts[i], fwd0, Vector3d.ZAxis);
                Plane firstFramePlane = new Plane(p0, fwd0, Vector3d.ZAxis);
                results[i].Xforms.Add(Transform.PlaneToPlane(results[i].BasePlane, firstFramePlane));

                // ✅ 修复点：添加 * 0.001 缩放，统一输出为米单位
                results[i].Vectors.Add(results[i].CurrentVel * 0.001);
            }

            int steps = (int)(Duration / TimeStep);
            bool[] collidedThisFrame = new bool[count];

            for (int f = 0; f < steps; f++)
            {
                Array.Clear(collidedThisFrame, 0, count);
                double currentTime = f * TimeStep;

                Parallel.For(0, count, i =>
                {
                    var res = results[i];

                    if (!res.IsActive)
                    {
                        if (currentTime >= res.ActivationTime)
                        {
                            res.IsActive = true;
                            res.CurrentVel = res.LaunchVel;
                        }
                        else
                        {
                            res.CurrentVel = Vector3d.Zero;
                            return;
                        }
                    }

                    if (res.StopTime >= 0)
                    {
                        res.CurrentVel = Vector3d.Zero;
                        return;
                    }

                    double r = radiuses[i];
                    double fric = Friction[i % Friction.Count];

                    Vector3d vel = res.CurrentVel;
                    Vector3d oldVel = vel;
                    Point3d pos = res.CurrentPos;
                    Vector3d newtonianVel = vel + gravityVec * TimeStep;
                    Point3d nextPos = pos + newtonianVel * TimeStep;

                    Vector3d n = SolvePositionAccumulated(Obstacles, ref nextPos, r, 3);
                    bool isColliding = n.Length > 0.001;
                    if (isColliding) n.Unitize(); else n = Vector3d.ZAxis;
                    bool isImpact = isColliding && !res.WasColliding;

                    if (isColliding)
                    {
                        double vProj = newtonianVel * n;
                        if (vProj < 0) newtonianVel -= n * vProj;
                    }
                    double dist = pos.DistanceTo(nextPos);
                    res.TotalDist += dist;

                    if (isImpact)
                    {
                        res.TotalME = 0.5 * newtonianVel.SquareLength + gVal * nextPos.Z;
                        res.AccLoss = 0.0;
                    }
                    else if (isColliding)
                    {
                        double normalForce = Math.Abs(gravityVec * n);
                        double fWork = (normalForce * fric) * dist;
                        double dWork = (airDragCoeff * vel.Length * vel.Length) * dist;
                        res.AccLoss += (fWork + dWork);
                    }
                    else
                    {
                        double dWork = (airDragCoeff * vel.Length * vel.Length) * dist;
                        res.AccLoss += dWork;
                    }

                    double curPE = gVal * nextPos.Z;
                    double targetKE = res.TotalME - curPE - res.AccLoss;
                    if (targetKE < 0) targetKE = 0;
                    double targetSpeed = Math.Sqrt(2.0 * targetKE);

                    if (isColliding && fric < 1e-6)
                    {
                        Vector3d slope = gravityVec - n * (gravityVec * n);
                        if (slope.Length > 1e-6) slope.Unitize();
                        Vector3d side = Vector3d.CrossProduct(n, slope);
                        if (side.Length > 1e-6) side.Unitize();
                        double vSide = newtonianVel * side;
                        Vector3d vSlopePart = newtonianVel - side * vSide;
                        double damping = 0.999;
                        Vector3d dir = vSlopePart + side * (vSide * damping);
                        if (dir.Length > 1e-6) dir.Unitize(); else dir = Vector3d.XAxis;
                        newtonianVel = dir * targetSpeed;
                    }
                    else
                    {
                        if (newtonianVel.Length > 1e-6) newtonianVel.Unitize();
                        else if (vel.Length > 1e-6) newtonianVel = vel / vel.Length;
                        else newtonianVel = Vector3d.XAxis;
                        newtonianVel *= targetSpeed;
                        if (isColliding && fric > 1e-6)
                        {
                            Vector3d slopeG = gravityVec - n * (gravityVec * n);
                            if (targetSpeed < 5.0 && slopeG.Length < gVal * fric) newtonianVel = Vector3d.Zero;
                        }
                    }

                    if (newtonianVel.Length > results[i].MaxSpeed) results[i].MaxSpeed = newtonianVel.Length;

                    Vector3d netAccel = (newtonianVel - oldVel) / TimeStep - gravityVec;
                    double gForce = netAccel.Length / gVal;
                    if (gForce > results[i].MaxG) results[i].MaxG = gForce;

                    if (newtonianVel.Length < 1.0 && isColliding)
                    {
                        if (res.StopTime < 0) res.StopTime = f * TimeStep;
                    }
                    else res.StopTime = -1;

                    res.CurrentPos = nextPos;
                    res.CurrentVel = newtonianVel;
                    res.WasColliding = isColliding;
                });

                int subIter = 8;
                for (int iter = 0; iter < subIter; iter++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        for (int j = i + 1; j < count; j++)
                        {
                            if (!results[i].IsActive || !results[j].IsActive) continue;

                            Point3d p1 = results[i].CurrentPos;
                            Point3d p2 = results[j].CurrentPos;
                            double rSum = radiuses[i] + radiuses[j];
                            double distSq = p1.DistanceToSquared(p2);

                            if (distSq < rSum * rSum)
                            {
                                double dist = Math.Sqrt(distSq);
                                if (dist < 0.1) dist = 0.1;
                                double overlap = rSum - dist;
                                Vector3d n = (p1 - p2) / dist;
                                double totalMass = masses[i] + masses[j];
                                double mRatio1 = masses[j] / totalMass;
                                double mRatio2 = masses[i] / totalMass;

                                Vector3d push = n * overlap;
                                results[i].CurrentPos += push * mRatio1;
                                results[j].CurrentPos -= push * mRatio2;
                                collidedThisFrame[i] = true;
                                collidedThisFrame[j] = true;
                            }
                        }
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    for (int j = i + 1; j < count; j++)
                    {
                        if (!results[i].IsActive || !results[j].IsActive) continue;

                        Point3d p1 = results[i].CurrentPos;
                        Point3d p2 = results[j].CurrentPos;
                        double rSum = radiuses[i] + radiuses[j];
                        double distSq = p1.DistanceToSquared(p2);

                        if (distSq < (rSum * rSum) * 1.001)
                        {
                            double dist = Math.Sqrt(distSq);
                            if (dist < 0.1) dist = 0.1;
                            Vector3d n = (p1 - p2) / dist;
                            Vector3d v1 = results[i].CurrentVel;
                            Vector3d v2 = results[j].CurrentVel;
                            Vector3d relVel = v1 - v2;
                            double velAlongNormal = relVel * n;

                            if (velAlongNormal < 0)
                            {
                                if (results[i].StopTime >= 0) results[i].StopTime = -1;
                                if (results[j].StopTime >= 0) results[j].StopTime = -1;

                                double e_combined = (eValues[i] + eValues[j]) * 0.5;
                                double jImpulse = -(1 + e_combined) * velAlongNormal;
                                jImpulse /= (1 / masses[i] + 1 / masses[j]);
                                Vector3d impulseVec = jImpulse * n;
                                results[i].CurrentVel += impulseVec / masses[i];
                                results[j].CurrentVel -= impulseVec / masses[j];
                            }
                        }
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    if (collidedThisFrame[i])
                    {
                        double pe = gVal * results[i].CurrentPos.Z;
                        double ke = 0.5 * results[i].CurrentVel.SquareLength;
                        results[i].TotalME = pe + ke;
                        results[i].AccLoss = 0.0;
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    var res = results[i];

                    if (!res.IsActive || res.StopTime >= 0)
                    {
                        if (res.Xforms.Count > 0) res.Xforms.Add(res.Xforms.Last());
                        else res.Xforms.Add(Transform.Identity);

                        res.Vectors.Add(Vector3d.Zero);
                        res.Speeds.Add(0.0);
                        res.GForces.Add(0.0);
                        continue;
                    }

                    Vector3d v = res.CurrentVel;
                    Vector3d fwd = v.Length > 10 ? v : Vector3d.XAxis; fwd.Unitize();
                    Plane currentPlane = new Plane(res.CurrentPos, fwd, Vector3d.ZAxis);
                    if (!currentPlane.IsValid) currentPlane = new Plane(res.CurrentPos, Vector3d.XAxis, Vector3d.YAxis);
                    res.Xforms.Add(Transform.PlaneToPlane(res.BasePlane, currentPlane));
                    res.Vectors.Add(v * 0.001);
                    res.Speeds.Add(v.Length * 0.001);
                    res.GForces.Add(results[i].MaxG);
                }
            }

            for (int i = 0; i < count; i++)
            {
                List<Point3d> pts = new List<Point3d>();
                foreach (var xf in results[i].Xforms)
                {
                    Point3d p = results[i].BasePlane.Origin;
                    p.Transform(xf);
                    pts.Add(p);
                }
                if (pts.Count > 1) results[i].Trajectory = new PolylineCurve(pts);

                if (detailed)
                {
                    StringBuilder log = new StringBuilder();
                    log.AppendLine("╔══════════════════════════════════╗");
                    log.AppendLine($"║   🎱  物理报告 (Billiards)       ║");
                    log.AppendLine("╚══════════════════════════════════╝");
                    log.AppendLine($"[ 统计数据 ]");
                    log.AppendLine($"• 运动总里程 : {results[i].TotalDist / 1000.0:F2} 米");
                    log.AppendLine($"• 最高速度   : {results[i].MaxSpeed / 1000.0:F2} 米/秒");
                    log.AppendLine($"• 最大过载(G): {results[i].MaxG:F2} G");
                    log.AppendLine("");
                    log.AppendLine($"[ 最终状态 ]");
                    log.AppendLine($"• 坐标位置   : {results[i].CurrentPos.X:F1}, {results[i].CurrentPos.Y:F1}, {results[i].CurrentPos.Z:F1}");
                    if (results[i].StopTime >= 0)
                        log.AppendLine($"• 停止时间   : {results[i].StopTime:F2} 秒 🛑");
                    else
                        log.AppendLine($"• 停止时间   : (未完全静止) ⏳");

                    double finalKE = 0.5 * (results[i].CurrentVel.Length / 1000.0) * (results[i].CurrentVel.Length / 1000.0);
                    double finalPE = (gVal / 1000.0) * (results[i].CurrentPos.Z / 1000.0);
                    double loss = results[i].AccLoss / 1000000.0;
                    double currentTotal = finalKE + finalPE + loss;

                    log.AppendLine($"[ 能量审计 ] (单位: 约 J/kg)");
                    log.AppendLine($"• 当前动能   : {finalKE:F4}");
                    log.AppendLine($"• 当前势能   : {finalPE:F4}");
                    log.AppendLine($"• 热能损耗   : {loss:F4}");
                    log.AppendLine($"• 系统总能   : {currentTotal:F4}");
                    log.AppendLine($"• 能量上限   : {(results[i].TotalME / 1000000.0):F4}");

                    results[i].Log = log.ToString();
                }
            }
            return results;
        }

        private Vector3d SolvePositionAccumulated(List<Brep> breps, ref Point3d currentPos, double radius, int iterations)
        {
            Vector3d finalAccumulatedNormal = Vector3d.Zero;
            for (int k = 0; k < iterations; k++)
            {
                Vector3d iterTotalPush = Vector3d.Zero;
                Vector3d iterNormalSum = Vector3d.Zero;
                int contactCount = 0;
                for (int i = 0; i < breps.Count; i++)
                {
                    Brep brep = breps[i];
                    if (brep == null) continue;
                    Point3d tmpCp; ComponentIndex ci; double u, v; Vector3d tmpNormal;
                    bool found = brep.ClosestPoint(currentPos, out tmpCp, out ci, out u, out v, radius * 1.5, out tmpNormal);
                    if (found)
                    {
                        double dist = currentPos.DistanceTo(tmpCp);
                        if (dist <= radius + 0.01)
                        {
                            Vector3d pushDir = currentPos - tmpCp;
                            if (pushDir.Length < 0.001) pushDir = tmpNormal;
                            pushDir.Unitize();
                            double penetration = radius - dist;
                            iterTotalPush += pushDir * penetration;
                            iterNormalSum += pushDir;
                            contactCount++;
                        }
                    }
                }
                if (contactCount > 0)
                {
                    currentPos += iterTotalPush * 0.5;
                    finalAccumulatedNormal = iterNormalSum;
                }
                else break;
            }
            return finalAccumulatedNormal;
        }

        public override Guid ComponentGuid => new Guid("25874fdf-cd04-4f08-a7ba-513e884d8d73");
        protected override System.Drawing.Bitmap Icon => Dung_Beetle.Properties.Resources.sliding_simulation_icon1;
    }
}