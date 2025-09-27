using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 门岗进出权限
    /// </summary>
    [Table("d_truck_door")]
    public class d_truck_door
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 主表
        /// </summary>
        public String Mid { get; set; }

        /// <summary>
        /// 编号
        /// </summary>
        public String code { get; set; }

        /// <summary>
        /// 门岗名称
        /// </summary>
        public String door { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime? date_begin { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? date_end { get; set; }

        /// <summary>
        /// 门岗进出
        /// </summary>
        public String inoutflag { get; set; } = "inandout";

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