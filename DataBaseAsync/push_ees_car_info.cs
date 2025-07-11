using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Base.Entity
{
    [Table("push_ees_car_info")]
    public class push_ees_car_info
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
        /// 车牌颜色
        /// </summary>
        public string LicencePlateColor { get; set; }

        /// <summary>
        /// 车牌号码
        /// </summary>
        public string LicencePlateNum { get; set; }

        /// <summary>
        /// 车辆识别代码
        /// </summary>
        public string VIN { get;set;}


        /// <summary>
        /// 燃料类型
        /// </summary>
        public string FuelType { get; set; }

        /// <summary>
        /// 排放标准
        /// </summary>
        public string EmissionStandard { get; set; }

        /// <summary>
        /// 超标原因
        /// </summary>
        public string ExceedReason { get; set; }

        /// <summary>
        /// 是否解除超标排放
        /// </summary>
        public bool IsRelease { get; set; }

        /// <summary>
        /// 下放时间
        /// </summary>
        public DateTime? DelegateTime { get; set; }


        /// <summary>
        /// 解除时间
        /// </summary>
        public DateTime? ReleaseTime { get; set; }
    }
}
