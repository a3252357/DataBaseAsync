using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 临时车辆基础信息
    /// </summary>
    [Table("d_temptruck")]
    public class d_temptruck
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 车牌号码(*)
        /// </summary>
        public String truck_ph { get; set; }

        /// <summary>
        /// 车牌类型
        /// </summary>
        public String truck_type { get; set; }

        /// <summary>
        /// 
        /// 原因附件
        /// </summary>
        public String reason_appendix { get; set; }

        /// <summary>
        /// 车牌颜色(*)
        /// </summary>
        public String ph_color { get; set; }

        /// <summary>
        /// 车辆卡类型(*)
        /// </summary>
        public String zl { get; set; }

        /// <summary>
        /// 车辆信息来源
        /// </summary>
        public String sources { get; set; }

        /// <summary>
        /// 申请通行门岗
        /// </summary>
        public String door { get; set; }

        /// <summary>
        /// 备注信息
        /// </summary>
        public String Note { get; set; }

        /// <summary>
        /// 开始日期
        /// </summary>
        public DateTime? date_begin { get; set; }

        /// <summary>
        /// 结束日期
        /// </summary>
        public DateTime? date_end { get; set; }

        /// <summary>
        /// 进出次数是否限制
        /// </summary>
        public String limit_num_flag { get; set; }

        /// <summary>
        /// 允许最大进出次数
        /// </summary>
        public Int32? most_in_out_num { get; set; }

        /// <summary>
        /// 上次进出时间
        /// </summary>
        public DateTime? last_in_out_time { get; set; }

        /// <summary>
        /// 在厂状态
        /// </summary>
        public String using_state { get; set; }

        /// <summary>
        /// 本次数据进出次数
        /// </summary>
        public Int32? InOutNum { get; set; }

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
        /// 中间表Id
        /// </summary>
        public String tempid { get; set; }

        /// <summary>
        /// 经办人
        /// </summary>
        public String transactor { get; set; }

        /// <summary>
        /// 经办人联系方式
        /// </summary>
        public String transactorphone { get; set; }

        /// <summary>
        /// 通知人
        /// </summary>
        public String notifyman { get; set; }

        /// <summary>
        /// 通知单位
        /// </summary>
        public String notifyunit { get; set; }

        /// <summary>
        /// 联系方式
        /// </summary>
        public String tel { get; set; }

        /// <summary>
        /// 原因类型
        /// </summary>
        public String reasontype { get; set; }

        /// <summary>
        /// 申请Id
        /// </summary>
        public String apply_id { get; set; }

        public String state { get; set; }
        public Int32? peoplecnt { get; set; }

        /// <summary>
        /// 是否人车核验
        /// </summary>
        public String facemark { get; set; }


    }
}