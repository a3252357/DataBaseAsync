using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 人车绑定
    /// </summary>
    [Table("d_truck_driverxiet")]
    public class d_truck_driverxiet
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 车辆ID
        /// </summary>
        public String BIND_ID { get; set; }

        /// <summary>
        /// 车辆ID
        /// </summary>
        public String TRUCK_ID { get; set; }

        /// <summary>
        /// 司机ID
        /// </summary>
        public String DRIVER_ID { get; set; }
        /// <summary>
        /// 状态
        /// </summary>
        public String state { get; set; }
        /// <summary>
        /// 来源
        /// </summary>
        public String sources { get; set; }
        /// <summary>
        /// 开始生效时间
        /// </summary>
        public DateTime startdate { get; set; }
        /// <summary>
        /// 结束生效时间
        /// </summary>
        public DateTime enddate { get; set; }
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
        /// 车辆类型（0-信息大楼员工车，2-主厂区员工车，3-产权车，4-外协车）
        /// </summary>
        public string carsort { get; set; }
        /// <summary>
        /// 通行门岗
        /// </summary>
        public String door { get; set; }
        /// <summary>
        /// 通行区域 
        /// </summary>
        public String throughArea { get; set; }
        /// <summary>
        /// 生效状态（0-不生效；1-生效）
        /// </summary>
        public String effectFlag { get; set; }

    }
}