using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Base.Entity
{
    [Table("push_no_road_machinery_info")]
    public class push_no_road_machinery_info
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
        /// 环保登记编码
        /// </summary>
        public string EPCode { get; set; }

        /// <summary>
        /// 车牌号码
        /// </summary>
        public string LicencePlateNum { get; set; }

        /// <summary>
        /// 生产时间
        /// </summary>
        public DateTime? ProductionTime { get; set; }

        /// <summary>
        /// 排放标准
        /// </summary>
        public string EmissionStandard { get; set; }


        /// <summary>
        /// 燃料类型
        /// </summary>
        public string FuelType { get; set; }

        /// <summary>
        /// 机械种类
        /// </summary>
        public string MachineryType { get; set; }

        /// <summary>
        /// 机械环保代码/产品识别码（PIN）
        /// </summary>
        public string EPIdentifyCode { get; set; }

        /// <summary>
        /// 机械型号
        /// </summary>
        public string MachineryModel { get; set; }

        /// <summary>
        /// 发动机型号
        /// </summary>
        public string EngineModel { get; set; }

        /// <summary>
        /// 发动机生产厂
        /// </summary>
        public string EngineManufacturer { get; set; }


        /// <summary>
        /// 发动机编号
        /// </summary>
        public string EngineSN { get; set; }


        /// <summary>
        /// 整车（机）铭牌 照片
        /// </summary>
        public string MachineryNameplate { get; set; }

        /// <summary>
        /// 发动机铭牌 照片
        /// </summary>
        public string EngineNameplate { get; set; }


        /// <summary>
        /// 机械环保信息标签 照片
        /// </summary>
        public string EPTag { get; set; }

        /// <summary>
        /// 所属人（单位）
        /// </summary>
        public string Owner { get; set; }

        /// <summary>
        /// 申报单位名称
        /// </summary>
        public string CompanyName { get; set; }

        /// <summary>
        /// 作业区域（地点）
        /// </summary>
        public string WorkArea { get; set; }

        /// <summary>
        /// 厂牌型号（车型）
        /// </summary>
        public string NonRoadMode { get; set; }

        /// <summary>
        /// 车架号
        /// </summary>
        public string VIN { get; set; }

        /// <summary>
        /// 是否安装定位系统，0-否，1-是
        /// </summary>
        public string PositionSysStatus { get; set; }

        /// <summary>
        /// 定位系统编码
        /// </summary>
        public string PositionSysCode { get; set; }

        /// <summary>
        /// 定位系统有效期yyyy-MM-dd
        /// </summary>
        public DateTime? PositionSysValidData { get; set; }

        /// <summary>
        /// 定位系统缴费凭证（PDF地址）
        /// </summary>
        public string PositionSysPayVoucherUrl { get; set; }

        /// <summary>
        /// 经办人联系电话
        /// </summary>
        public string HandlerMobile { get; set; }

        /// <summary>
        /// 服务合同url
        /// </summary>
        public string ContractUrl { get; set; }

        /// <summary>
        /// 车辆注册有效期yyyy-MM-dd
        /// </summary>
        public DateTime? NonRoadRegisterValidData { get; set; }
    }
}
