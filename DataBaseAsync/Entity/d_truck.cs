using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 车辆基础信息
    /// </summary>
    [Table("d_truck")]
    public class d_truck
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public string Id { get; set; }

        /// <summary>
        /// 车牌号码(*)
        /// </summary>
        public string truck_ph { get; set; }
        /// <summary>
        /// 挂车车牌号(*)
        /// </summary>
        public string trailer_ph { get; set; }
        /// <summary>
        /// 车牌颜色(*)
        /// </summary>
        public string ph_color { get; set; }

        /// <summary>
        /// 车辆卡类型(*)
        /// </summary>
        public string zl { get; set; }

        /// <summary>
        /// 车辆照片
        /// </summary>
        public string photourl { get; set; }

        /// <summary>
        /// 车辆状态
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 车辆锁定黑名单时状态
        /// </summary>
        public string BlackLockTimeStatus { get; set; }
        /// <summary>
        /// 车辆状态
        /// </summary>
        public string gsbz { get; set; }

        /// <summary>
        /// 车辆品牌
        /// </summary>
        public string truck_brand { get; set; }

        /// <summary>
        /// 车辆类型(*)
        /// </summary>
        public string truck_type { get; set; }

        /// <summary>
        /// 车辆颜色
        /// </summary>
        public string truck_color { get; set; }

        /// <summary>
        /// 车辆自重
        /// </summary>
        public string truck_weight { get; set; } = "0";

        /// <summary>
        /// 行驶证号码
        /// </summary>
        public string truck_bh { get; set; }

        /// <summary>
        /// 行驶证号码
        /// </summary>
        public string walking_path { get; set; }

        /// <summary>
        /// 车主姓名
        /// </summary>
        public string owner_id { get; set; }

        /// <summary>
        /// 车主身份证
        /// </summary>
        public string owner_sfzh { get; set; }

        /// <summary>
        /// 车主联系方式：
        /// </summary>
        public string owner_telephone { get; set; }

        /// <summary>
        /// 车辆所属性质
        /// </summary>
        public string truck_properties { get; set; }


        /// <summary>
        /// 车辆资产
        /// </summary>
        public string truck_assets { get; set; }

        /// <summary>
        /// 车辆信息来源
        /// </summary>
        public string sources { get; set; }

        /// <summary>
        /// 关联服务单位信息
        /// </summary>
        public string qwdw { get; set; }

        /// <summary>
        /// 关联所属单位信息
        /// </summary>
        public string dwdm { get; set; }

        /// <summary>
        /// 备注信息
        /// </summary>
        public string Note { get; set; }

        /// <summary>
        /// 开始日期
        /// </summary>
        public DateTime? date_begin { get; set; }

        /// <summary>
        /// 结束日期
        /// </summary>
        public DateTime? date_end { get; set; }

        /// <summary>
        /// 上次进出时间
        /// </summary>
        public DateTime? last_in_out_time { get; set; }

        /// <summary>
        /// 进出次数是否限制
        /// </summary>
        public string limit_num_flag { get; set; }

        /// <summary>
        /// 允许最大进出次数
        /// </summary>
        public Int32? most_in_out_num { get; set; }

        /// <summary>
        /// 在厂状态
        /// </summary>
        public string using_state { get; set; }

        /// <summary>
        /// 进出次数
        /// </summary>
        public Int32? InOutNum { get; set; }

        /// <summary>
        /// 是否锁定G卡
        /// </summary>
        public string GLock { get; set; } = "0";

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
        /// 修改人
        /// </summary>
        public string tempid { get; set; }

        /// <summary>
        /// 经办人
        /// </summary>
        public string transactor { get; set; }

        /// <summary>
        /// 经办人
        /// </summary>
        public string transactorphone { get; set; }

        /// <summary>
        /// 车架号
        /// </summary>
        public string VIN { get; set; }
        /// <summary>
        /// 发动机号
        /// </summary>
        public string EngineNo { get; set; }
        /// <summary>
        /// 发动机编号
        /// </summary>
        public string EngineSerialNo { get; set; }
        /// <summary>
        /// 燃油类型
        /// </summary>
        public string FuelType { get; set; }
        /// <summary>
        /// 排放标准
        /// </summary>
        public string EmissionStandard { get; set; }
        /// <summary>
        /// 危险品车辆
        /// </summary>
        public string DangerousGoods { get; set; }
        /// <summary>
        /// 标签
        /// </summary>
        public string Tag { get; set; }
        /// <summary>
        /// 是否为运输设备、辅材、备件车辆
        /// </summary>
        public string istransportcar { get; set; }

        /// <summary>
        /// 是否为非道路机具
        /// </summary>
        public string isroadcar { get; set; }

        /// <summary>
        /// 随车环保清单url
        /// </summary>
        public string Environmentallist { get; set; }

        /// <summary>
        /// 行驶证url
        /// </summary>
        public string Drivinglicense { get; set; }

        /// <summary>
        /// 营业执照url
        /// </summary>
        public string Busineslicenseurl { get; set; }
        /// <summary>
        /// 营业执照url
        /// </summary>
        public string sidephotourl { get; set; }
        /// <summary>
        /// 营业执照url
        /// </summary>
        public string backpgotourl { get; set; }
        /// <summary>
        /// 营业执照url
        /// </summary>
        public string ruckload { get; set; }
        /// <summary>
        /// 营业执照url
        /// </summary>
        public string SHR_ID { get; set; }
        /// <summary>
        /// 营业执照url
        /// </summary>
        public string SHR_DEPART { get; set; }

        /// <summary>
        /// 营业执照url
        /// </summary>
        public string isregister { get; set; } = "0";
        /// <summary>
        /// 车辆注册时间
        /// </summary>
        public DateTime? regcardate { get; set; }
        /// <summary>
        /// 黑名单原因
        /// </summary>
        [NotMapped]
        public string blackreason { get; set; }
        /// <summary>
        /// 黑名单原因
        /// </summary>
        [NotMapped]
        public DateTime? blacktime { get; set; }
        /// <summary>
        /// 车辆注册时间
        /// </summary>
        public string framenumber { get; set; }
        /// <summary>
        /// 车辆注册时间
        /// </summary>
        public string approveurl { get; set; }
        /// <summary>
        /// 车辆注册时间
        /// </summary>
        public string OBD { get; set; }
        /// <summary>
        /// 年检有效期
        /// </summary>
        public DateTime? AnnualPeriod { get; set; }
        /// <summary>
        /// 保险有效期
        /// </summary>
        public DateTime? InsurancePeriod { get; set; }
        /// <summary>
        /// 保险照片
        /// </summary>
        public string InsurancePhoto { get; set; }
        /// <summary>
        /// 进出厂申请id
        /// </summary>
        public string apply_id { get; set; }
        /// <summary>
        /// 协同车辆Id
        /// </summary>
        public string truckid { get; set; }
        /// <summary>
        /// 服务单位编号
        /// </summary>
        public string serunitno { get; set; }
        /// <summary>
        /// 车辆高度
        /// </summary>
        public string truck_height { get; set; } = "0";
        /// <summary>
        /// 限载人数
        /// </summary>
        public string capacitylimit { get; set; } = "0";
        /// <summary>
        /// 是否需要人车核验（0，需要，1，不需要）
        /// </summary>
        public string facemark { get; set; } = "0";

        /// <summary>
        /// 行驶证副本
        /// </summary>
        public string xszBack { get; set; }

        /// <summary>
        /// 危险拉运资格证
        /// </summary>
        public string wxp { get; set; }

        /// <summary>
        /// 电子年检
        /// </summary>
        public string dznj { get; set; }

        /// <summary>
        /// 车辆使用状态（0-正常，1-过期，2-待用）
        /// </summary>
        public string usestate { get; set; }

        /// <summary>
        /// 车辆状态（0-正常，1-限制，2黑名单，3-注销）
        /// </summary>
        public string carzt { get; set; }

        /// <summary>
        /// 车辆种类（0-员工车，1-产权车，2-外协车）
        /// </summary>
        public string carsort { get; set; }

        /// <summary>
        /// 员工车分类（0-领导车，1-普通员工车）
        /// </summary>
        public string ygcsort { get; set; }

        /// <summary>
        /// 产权车分类（0-客车，1-货车，2-通勤车）
        /// </summary>
        public string cqcsort { get; set; }

        /// <summary>
        /// 外协车分类（0-客车，1-货车，2-通勤车）
        /// </summary>
        public string wxcsort { get; set; }


        /// <summary>
        /// 磁芯证号
        /// </summary>
        public string core { get; set; }

        /// <summary>
        /// 审批人
        /// </summary>
        public string createBy { get; set; }

        /// <summary>
        /// 申请创建时间
        /// </summary>
        public DateTime? sqCreatTime { get; set; }

        /// <summary>
        /// xl用工状态
        /// </summary>
        public string xlcarstate { get; set; }
        /// <summary>
        ///申请人
        /// </summary>
        public string SQR { get; set; }
        /// <summary>
        /// 申请时间
        /// </summary>
        public DateTime? SQSJ { get; set; }
        /// <summary>
        ///审核人
        /// </summary>
        public string SHR { get; set; }
        /// <summary>
        /// 审核时间
        /// </summary>
        public DateTime? SHSJ { get; set; }
        //通行区域
        public string throughArea { get; set; }
        public string votext { get; set; }
    }
}