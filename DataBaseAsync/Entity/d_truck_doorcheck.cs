using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 车辆通行自定义条件
    /// </summary>
    [Table("d_truck_doorcheck")]
    public class d_truck_doorcheck
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 字段
        /// </summary>
        public String Filed { get; set; }

        /// <summary>
        /// 数据源条件
        /// </summary>
        public String FiledCondition { get; set; }

        /// <summary>
        /// 参数值
        /// </summary>
        public String FiledValue { get; set; }

        /// <summary>
        /// 是否强制通行
        /// </summary>
        public String CanForceApprove { get; set; }

        /// <summary>
        /// 提示语句
        /// </summary>
        public String Msg { get; set; }

        /// <summary>
        /// 开始生效事件
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 介绍生效事件
        /// </summary>
        public DateTime? EndTime { get; set; }

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