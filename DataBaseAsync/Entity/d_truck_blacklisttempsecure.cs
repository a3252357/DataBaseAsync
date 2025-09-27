using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 黑名单临时解除
    /// </summary>
    [Table("d_truck_blacklisttempsecure")]
    public class d_truck_blacklisttempsecure
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 车牌号码
        /// </summary>
        public String truck_ph { get; set; }

        /// <summary>
        /// 车牌颜色
        /// </summary>
        public String ph_color { get; set; }

        /// <summary>
        /// 临时解除人
        /// </summary>
        public String secureuser { get; set; }

        /// <summary>
        /// 临时解除开始时间
        /// </summary>
        public DateTime? securestarttime { get; set; }

        /// <summary>
        /// 临时解除结束时间
        /// </summary>
        public DateTime? secureendtime { get; set; }

        /// <summary>
        /// 临时解除原因
        /// </summary>
        public String securereason { get; set; }

        /// <summary>
        /// 临时解除附件
        /// </summary>
        public String secureappendix { get; set; }

        /// <summary>
        /// 临时解除状态
        /// </summary>
        public String securestate { get; set; } = "0";

        /// <summary>
        /// 临时解除次数
        /// </summary>
        public Int32? securenum { get; set; }

        /// <summary>
        /// 出厂次数
        /// </summary>
        public Int32? outnum { get; set; } = 0;

        /// <summary>
        /// 申请部门
        /// </summary>
        public String depart { get; set; }

        /// <summary>
        /// 车辆状态
        /// </summary>
        public String truck_state { get; set; }

        /// <summary>
        /// 黑名单原因
        /// </summary>
        public String black_reason { get; set; }

        /// <summary>
        /// 黑名单附件
        /// </summary>
        public String black_appendix { get; set; }

        /// <summary>
        /// 违规时间
        /// </summary>
        public DateTime? Violation_time { get; set; }

        /// <summary>
        /// 违规地点
        /// </summary>
        public String place { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public String Remark { get; set; }

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

        /// <summary>
        /// 来源
        /// </summary>
        public String sources { get; set; }

        /// <summary>
        /// 解锁原因
        /// </summary>
        public String unlock_reason { get; set; }

        /// <summary>
        /// 解锁附件
        /// </summary>
        public String unlock_appendix { get; set; }

    }
}