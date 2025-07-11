using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Coldairarrow.bgmj.Base.Entity
{
    [Table("push_pollution_warning_info")]
    public class push_pollution_warning_info
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public string Id { get; set; }


        /// <summary>
        /// 第三方Id
        /// </summary>
        public string ThirdId { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string Remark { get; set; }

        /// <summary>
        /// 使用状态
        /// </summary>
        public string StatusFlag { get; set; }

        /// <summary>
        /// 创建日期
        /// </summary>
        public DateTime? CreateDate { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public string CreateOperator { get; set; }

        /// <summary>
        /// 修改日期
        /// </summary>
        public DateTime? ModifyDate { get; set; }

        /// <summary>
        /// 修改人
        /// </summary>
        public string ModifyOperator { get; set; }

        /// <summary>
        /// 企业编号
        /// </summary>
        public string EnterpriseNum { get; set; }

        /// <summary>
        /// 企业名称
        /// </summary>
        public string EnterpriseName { get; set; }


        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }


        /// <summary>
        /// 解除时间
        /// </summary>
        public DateTime EndTime { get; set; }


        /// <summary>
        /// 预警级别
        /// </summary>
        public string WarningLevel { get; set; }

        /// <summary>
        /// 响应级别
        /// </summary>
        public string ResponseLevel { get; set; }


        /// <summary>
        /// 管控措施
        /// </summary>
        public string ControlMeasures { get; set; }
    }
}
