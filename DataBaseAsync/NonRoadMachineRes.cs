using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Coldairarrow.bgmj.Base.Entity
{

    /// <summary>
    /// 非道路移动机械预约表实体
    /// </summary>
    [Table("d_nonroad_macres")]
    public class NonRoadMachineRes
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public string Id { get; set; }

        /// <summary>
        /// 非道路移动机械预约编码
        /// </summary>
        public string NRoadNo { get; set; }

        /// <summary>
        /// 车辆PIN编码：绝对的唯一标识
        /// </summary>
        public string VehiclePIN { get; set; }

        /// <summary>
        /// 环保登记编码
        /// </summary>
        public string ERCode { get; set; }

        /// <summary>
        /// 进出厂状态 1—进厂；2—出厂
        /// </summary>
        public int? ExitStatus { get; set; }

        /// <summary>
        /// 预约状态 0预约 1完成
        /// </summary>
        public int? ResStatus { get; set; }
        
        /// <summary>
        /// 预约进厂日期
        /// </summary>
        public DateTime? ENDate { get; set; }

        /// <summary>
        /// 预约出厂日期
        /// </summary>
        public DateTime? ExitDate { get; set; }

        /// <summary>
        /// 进厂门岗
        /// </summary>
        public string GateInName { get; set; }

        /// <summary>
        /// 进门岗CODE
        /// </summary>
        public string GateInCode { get; set; }

        /// <summary>
        /// 出厂门岗
        /// </summary>
        public string GateOutName { get; set; }

        /// <summary>
        /// 出门岗CODE
        /// </summary>
        public string GateOutCode { get; set; }

        /// <summary>
        /// 活动区域
        /// </summary>
        public string WorkArea { get; set; }

        /// <summary>
        /// 活动内容
        /// </summary>
        public string WorkContent { get; set; }

        /// <summary>
        /// 预约服务企业名称
        /// </summary>
        public string CompanyName { get; set; }

        /// <summary>
        /// 预约人ID
        /// </summary>
        public string ReservationId { get; set; }

        /// <summary>
        /// 预约人姓名
        /// </summary>
        public string ReservationName { get; set; }


        /// <summary>
        /// 实际通行时间
        /// </summary>
        public DateTime? PassTime { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public string CreateOperator { get; set; }

        /// <summary>
        /// 创建日期
        /// </summary>
        public DateTime CreateDate { get; set; }

        /// <summary>
        /// 修改人
        /// </summary>
        public string ModifyOperator { get; set; }

        /// <summary>
        /// 修改日期
        /// </summary>
        public DateTime? ModifyDate { get; set; }

        /// <summary>
        /// 填表时间（创建时间）
        /// </summary>
        public DateTime? CreateTime { get; set; }

        /// <summary>
        /// 定位系统编码
        /// </summary>
        public string PositionSysCode { get; set; }

        /// <summary>
        /// 起点位置
        /// </summary>
        public string StartPosition { get; set; }

        /// <summary>
        /// 终点位置
        /// </summary>
        public string EndPosition { get; set; }

        /// <summary>
        /// 是否出厂，0-否，1-是
        /// </summary>
        public string IsExit { get; set; }

        /// <summary>
        /// 填表人电话
        /// </summary>
        public string ReservationByMobile { get; set; }

        /// <summary>
        /// 司机 B2 驾驶证，url
        /// </summary>
        public string DriveLicence { get; set; }

        /// <summary>
        /// 司机姓名
        /// </summary>
        public string DriverName { get; set; }

        /// <summary>
        /// 司机身份证号
        /// </summary>
        public string DriverIdNo { get; set; }

        /// <summary>
        /// 司机特种车辆操作证
        /// </summary>
        public string DriverOperateCert { get; set; }

    }
}

