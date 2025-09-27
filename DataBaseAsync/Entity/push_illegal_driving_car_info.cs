using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Coldairarrow.bgmj.Base.Entity
{
    [Table("push_illegal_driving_car_info")]
    public class push_illegal_driving_car_info
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
        public string VIN { get; set; }

        /// <summary>
        /// 进厂时间
        /// </summary>
        public DateTime? EnterTime { get; set; }

        /// <summary>
        /// 出厂时间
        /// </summary>
        public DateTime? LeaveTime { get; set; }

        /// <summary>
        /// 违规通行原因
        /// </summary>
        public string ViolationReason { get; set; }

        /// <summary>
        /// 其他说明
        /// </summary>
        public string OtherExplain { get; set; }
    }
}
