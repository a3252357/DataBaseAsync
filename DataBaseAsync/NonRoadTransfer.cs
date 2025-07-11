using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Coldairarrow.bgmj.Base.Entity
{

    /// <summary>
    /// 非道路移动机械转场信息实体类
    /// </summary>
    [Table("d_nonroad_transfer")] 
    public class NonRoadTransfer
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public string ID { get; set; }

        /// <summary>
        /// 预约编号
        /// </summary>
        [Column("NonRoadNum")]
        public string non_road_num { get; set; } // 预约编号

        /// <summary>
        /// 转场填表人姓名
        /// </summary>
        [Column("TransferName")]
        public string transfer_name { get; set; } // 转场填表人姓名

        /// <summary>
        ///  转场填表人电话
        /// </summary>
        [Column("TransferMobile")]
        public string transfer_mobile { get; set; } // 转场填表人电话

        /// <summary>
        /// 转场地点
        /// </summary>
        [Column("TransferPlace")]
        public string transfer_place { get; set; } // 转场地点

        /// <summary>
        /// 转场截止时间（年-月-日-时）
        /// </summary>
        [Column("EndTime")]
        public DateTime end_time { get; set; } // 转场截止时间（年-月-日-时）

        /// <summary>
        /// 转场事由
        /// </summary>
        [Column("TransferReason")]
        public string transfer_reason { get; set; } // 转场事由
        /// <summary>
        /// 是否往返（0-否，1-是）
        /// </summary>
        [Column("RoundStatus")]
        public string round_status { get; set; } // 是否往返（0-否，1-是）
        /// <summary>
        /// 创建日期
        /// </summary>
        [Column("CreateDate")]
        public DateTime CreateDate { get; set; } // 创建日期

        /// <summary>
        /// 创建人
        /// </summary>
        [Column("CreateOperator")]
        public string CreateOperator { get; set; } // 创建人

        /// <summary>
        /// 修改日期（可为空）
        /// </summary>
        [Column("ModifyDate")]
        public DateTime? ModifyDate { get; set; } // 修改日期（可为空）
        /// <summary>
        /// 修改人
        /// </summary>
        [Column("ModifyOperator")]
        public string ModifyOperator { get; set; } // 修改人
    }
}

