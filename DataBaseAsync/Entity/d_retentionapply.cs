using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 滞留车辆滞留申请表
    /// </summary>
    [Table("d_retentionapply")]
    public class d_retentionapply
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 车牌号
        /// </summary>
        public String truck_ph { get; set; }

        /// <summary>
        /// 车牌颜色
        /// </summary>
        public String ph_color { get; set; }

        /// <summary>
        /// 进出Id
        /// </summary>
        public String InOutId { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }
        /// <summary>
        /// 锁定状态
        /// </summary>
        public String LockStatus { get; set; }

        /// <summary>
        /// 申请时间
        /// </summary>
        public DateTime? ApproveTime { get; set; }
        /// <summary>
        /// 是否七天以上
        /// </summary>
        public string IS_OVER_7 { get; set; }
        /// <summary>
        /// 派单人ID
        /// </summary>
        public string PDR_ID { get; set; }
        /// <summary>
        /// 板块负责人ID
        /// </summary>
        public string MANAGER_ID { get; set; }
        /// <summary>
        /// 锁定状态
        /// </summary>
        public String TRUCK_ID { get; set; }
        /// <summary>
        /// 锁定状态
        /// </summary>
        public String SHR_ID { get; set; }
        /// <summary>
        /// 锁定状态
        /// </summary>
        public String SHR_DEPART { get; set; }
        /// <summary>
        /// 锁定状态
        /// </summary>
        public String Reasons_detention { get; set; }
        /// <summary>
        /// 锁定状态
        /// </summary>
        public String Work_area { get; set; }

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