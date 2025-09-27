using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.CHCSDK.Entity
{
    /// <summary>
    /// 门禁主机设备卡数据
    /// </summary>
    [Table("monitor_accesscontroldevicecard")]
    //[LogicDelete("Status","5")]
    public class monitor_accesscontroldevicecard
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 设备ID
        /// </summary>
        public String DeviceId { get; set; }

        /// <summary>
        /// 卡号
        /// </summary>
        public String CardNo { get; set; }

        /// <summary>
        /// 员工代号
        /// </summary>
        public String EmployeeNo { get; set; }

        /// <summary>
        /// 员工名称
        /// </summary>
        public String EmployeeName { get; set; }

        /// <summary>
        /// 门岗编号
        /// </summary>
        public String doors { get; set; } = "";

        /// <summary>
        /// 版本
        /// </summary>
        public Int32 version { get; set; } = 0;

        /// <summary>
        /// 计划模板
        /// </summary>
        public String CardRightPlan { get; set; } = "1";

        /// <summary>
        /// 生效时间
        /// </summary>
        public DateTime? struBeginTime { get; set; }

        /// <summary>
        /// 失效时间
        /// </summary>
        public DateTime? struEndTime { get; set; }

        /// <summary>
        /// 卡类型
        /// </summary>
        public Int32 CardType { get; set; } = 1;

        /// <summary>
        /// 卡状态
        /// </summary>
        public String Status { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public String ReMark { get; set; }

        /// <summary>
        /// 使用状态
        /// </summary>
        public String StatusFlag { get; set; }

        /// <summary>
        /// 创建日期
        /// </summary>
        public DateTime? CreateDate { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public String CreateOperator { get; set; }

        /// <summary>
        /// 修改日期
        /// </summary>
        public DateTime? ModifyDate { get; set; }

        /// <summary>
        /// 修改人
        /// </summary>
        public String ModifyOperator { get; set; }

    }
}