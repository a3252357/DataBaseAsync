
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 黑名单
    /// </summary>
    [Table("d_truck_blacklist")]
    public class d_truck_blacklist
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public String black_no { get; set; }
        /// <summary>
        /// 车牌号码
        /// </summary>
        public String truck_ph { get; set; }

        /// <summary>
        /// 车牌颜色
        /// </summary>
        public String ph_color { get; set; }

        /// <summary>
        /// 车辆状态
        /// </summary>
        public String truck_state { get; set; }

        /// <summary>
        /// 黑名单申请部门
        /// </summary>
        public String depart { get; set; }

        /// <summary>
        /// 黑名单原因
        /// </summary>
        public String black_reason { get; set; }

        /// <summary>
        /// 黑名单附件
        /// </summary>
        public String black_appendix { get; set; }

        /// <summary>
        /// 解锁原因(*)
        /// </summary>
        public String unlock_reason { get; set; }

        /// <summary>
        /// 解锁附件
        /// </summary>
        public String unlock_appendix { get; set; }
        /// <summary>
        /// 违规时间
        /// </summary>
        public String Violation_type { get; set; }

        [NotMapped]
        /// <summary>
        /// 排放标准
        /// </summary>
        public String EmissionStandard { get; set; }
        [NotMapped]
        /// <summary>
        /// 排放标准
        /// </summary>
        public String TempUnLockState { get; set; }

        [NotMapped]
        /// <summary>
        /// 解/锁卡次数统计
        /// </summary>
        public Int32 LOCKNUM { get; set; } = 0;

        [NotMapped]
        /// <summary>
        /// 解/锁卡次数统计
        /// </summary>
        public Int32 UNLOCKNUM { get; set; } = 0;

        /// <summary>
        /// 违规时间
        /// </summary>
        public DateTime? Violation_time { get; set; }

        /// <summary>
        /// 违规时间
        /// </summary>
        public DateTime? LOUTIME { get; set; }

        /// <summary>
        /// 违规时间
        /// </summary>
        public DateTime? OPTIME { get; set; }

        /// <summary>
        /// 违规时间
        /// </summary>
        public String OPUSER { get; set; }

        /// <summary>
        /// 临解次数
        /// </summary>
        public Int32? TEMP_UNLOCK { get; set; }=0;
        /// <summary>
        /// 违规地点
        /// </summary>
        public String place { get; set; }
        /// <summary>
        /// 违规地点
        /// </summary>
        public String violationid{ get; set; }

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

    }
}