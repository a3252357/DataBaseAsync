using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 人员进出日志
    /// </summary>
    [Table("d_man_logfacecheck")]
    public class d_man_logfacecheck
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 姓名
        /// </summary>
        public String name { get; set; }

        /// <summary>
        /// 性别
        /// </summary>
        public String sex { get; set; }

        /// <summary>
        /// 身份证号
        /// </summary>
        public String sfzh { get; set; }

        /// <summary>
        /// photourl
        /// </summary>
        public String photourl { get; set; }

        /// <summary>
        /// 门岗
        /// </summary>
        public String door { get; set; }

        /// <summary>
        /// 开始日期
        /// </summary>
        public DateTime? date_begin { get; set; }

        /// <summary>
        /// 结束日期
        /// </summary>
        public DateTime? date_end { get; set; }

        /// <summary>
        /// 人员卡类型
        /// </summary>
        public String zl { get; set; }

        /// <summary>
        /// 人员卡状态
        /// </summary>
        public String Status { get; set; }

        /// <summary>
        /// 服务单位
        /// </summary>
        public String qwdw { get; set; }

        /// <summary>
        /// 所属单位
        /// </summary>
        public String dwdm { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public String Note { get; set; }

        /// <summary>
        /// 进厂时间
        /// </summary>
        public DateTime? come_in_time { get; set; }

        /// <summary>
        /// 离厂时间
        /// </summary>
        public DateTime? depart_from_time { get; set; }

        /// <summary>
        /// 门岗通道
        /// </summary>
        public String pass_num { get; set; }

        /// <summary>
        /// 进出状态
        /// </summary>
        public String data_flag { get; set; }

        /// <summary>
        /// 进出次数是否限制
        /// </summary>
        public String limit_num_flag { get; set; }

        /// <summary>
        /// 允许最大进出次数
        /// </summary>
        public Int32? most_in_out_num { get; set; }

        /// <summary>
        /// 在厂状态
        /// </summary>
        public String using_state { get; set; }

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
        /// 标签
        /// </summary>
        public String Tag { get; set; }

    }
}