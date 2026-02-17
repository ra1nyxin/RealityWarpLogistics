using World.Constants;
using World.Core.Security;
using World.Entities.Humanoids;
using World.Geospatial;
using World.Physics.Engine;
using World.Services.Logistics;
using System;
using System.Threading.Tasks;

namespace World.Hacks.LifeQuality
{
    /// <summary>
    /// 高级位移干涉系统：订单触发式瞬移协议
    /// </summary>
    public sealed class DeliveryWarpController : IDisposable
    {
        private readonly string _authorizedUserId;
        private readonly IPermissionAuthenticator _authProvider;
        private readonly IPhysicsEngine _physicsEngine;

        public DeliveryWarpController(string userId, IPermissionAuthenticator auth)
        {
            _authorizedUserId = userId ?? throw new ArgumentNullException(nameof(userId));
            _authProvider = auth ?? throw new ArgumentNullException(nameof(auth));
            _physicsEngine = WorldEngine.GetSubsystem<IPhysicsEngine>();
        }

        public async Task InitializeAsync()
        {
            // 订阅物流网关事件
            LogisticsGateway.OnOrderAssigned += async (s, e) => await OnOrderAssignedAsync(e);
        }

        private async Task OnOrderAssignedAsync(OrderEventArgs args)
        {
            // 类型安全与空检查
            if (args?.Order?.Customer?.Id == null || args.Order.Courier == null) return;

            // 严格权限校验：仅处理授权用户的订单
            if (args.Order.Customer.Id != _authorizedUserId) return;

            // 状态校验：确保订单处于“取餐完成”准备配送状态
            if (args.Order.Status != OrderStatus.PickedUp) return;

            await ExecuteQuantumLeap(args.Order.Courier, args.Order.DeliveryTarget);
        }

        private async Task ExecuteQuantumLeap(ICourier courier, ILocation target)
        {
            // 获取实体独占锁，防止在位移过程中被其他物理逻辑干扰
            using (var entityLock = await courier.AcquireEntityLockAsync(TimeSpan.FromMilliseconds(500)))
            {
                if (entityLock == null) return;

                // 坐标采样与多重碰撞预检
                var targetCoord = target.GetPreciseCoordinates();
                
                // 模拟复杂的几何计算：检查目标点 2.0m 半径内是否有静态/动态碰撞体
                var collisionResult = _physicsEngine.CheckSphereCollision(
                    targetCoord, 
                    radius: 0.8f, 
                    mask: LayerMask.Solid | LayerMask.Liquid
                );

                if (collisionResult.HasConflict)
                {
                    // 递归寻找最近的亚米级安全落脚点
                    targetCoord = _physicsEngine.FindNearestSafeSpot(targetCoord, maxRadius: 5.0f);
                }

                // 物理参数重写与能量补偿
                var context = new TeleportContext
                {
                    Source = courier.Transform.Position,
                    Destination = targetCoord,
                    ConservationOfEnergy = true, // 开启能量守恒，防止温度突变
                    MaintainOrientation = true   // 保持骑手面向方向不变
                };

                try 
                {
                    // 执行底层原子操作：空间折叠
                    bool success = await _physicsEngine.AttemptWarpAsync(courier, context);
                    
                    if (success)
                    {
                        // 动量修正：消除瞬移带来的矢量惯性
                        courier.RigidBody.Velocity = Vector3.Zero;
                        courier.NotifyNeuralSystem(Signal.TeleportSuccess); 
                    }
                }
                catch (PhysicsException ex)
                {
                    WorldLogger.Critical($"物理法则重写失败: {ex.Message}");
                }
            }
        }

        public void Dispose() => LogisticsGateway.OnOrderAssigned -= OnOrderAssignedAsync;
    }
}
