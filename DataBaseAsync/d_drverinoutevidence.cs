using Coldairarrow.bgmj.Base.Entity;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 进出厂凭据
    /// </summary>
    [Table("d_drverinoutevidence")]
    public class d_drverinoutevidence
    {

        /// <summary>
        /// Id
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 预约单ID
        /// </summary>
        public String ReservationNo { get; set; }

        /// <summary>
        /// 进出单号
        /// </summary>
        public String InOutNo { get; set; }

        /// <summary>
        /// 进出
        /// </summary>
        public String InOut { get; set; }

        /// <summary>
        /// 预约车牌号码
        /// </summary>
        public String truck_ph { get; set; }

        /// <summary>
        /// 预约车牌颜色
        /// </summary>
        public String ph_color { get; set; }

        /// <summary>
        /// 通行日期
        /// </summary>
        public DateTime passagedate { get; set; }

        /// <summary>
        /// 物资种类
        /// </summary>
        public String goodstype { get; set; }

        /// <summary>
        /// 物资名称
        /// </summary>
        public String goodsname { get; set; }

        /// <summary>
        /// 规格型号
        /// </summary>
        public String descsize { get; set; }

        /// <summary>
        /// 数量
        /// </summary>
        public Decimal? quantity { get; set; }

        /// <summary>
        /// 计量单位
        /// </summary>
        public String unit { get; set; }

        /// <summary>
        /// 进门岗
        /// </summary>
        public String indoor { get; set; }

        /// <summary>
        /// 出门岗
        /// </summary>
        public String outdoor { get; set; }

        /// <summary>
        /// 物资运输方向
        /// </summary>
        public String goodsdirection { get; set; }

        /// <summary>
        /// 运送地点
        /// </summary>
        public String goodsplace { get; set; }

        /// <summary>
        /// 派单单位
        /// </summary>
        public String depart { get; set; }

        /// <summary>
        /// 派单人员
        /// </summary>
        public String dispathpeople { get; set; }

        /// <summary>
        /// 派单时间
        /// </summary>
        public DateTime dispathtime { get; set; }

        /// <summary>
        /// 实际通行门岗
        /// </summary>
        public String realindoor { get; set; }

        /// <summary>
        /// 预约生成时间
        /// </summary>
        public DateTime? appointmentdate { get; set; }

        /// <summary>
        /// 通行时间
        /// </summary>
        public DateTime? doortime { get; set; }

        /// <summary>
        /// 通行照片
        /// </summary>
        public String doorphotourl { get; set; }

        /// <summary>
        /// 车斗照片
        /// </summary>
        public String hopperphotourl { get; set; }

        /// <summary>
        /// 车尾照片 bylqb
        /// </summary>
        public String tailphotourl { get; set; }

        /// <summary>
        /// 录制Url
        /// </summary>
        public String recordurl { get; set; }

        /// <summary>
        /// 车架号
        /// </summary>
        public String VIN { get; set; }

        /// <summary>
        /// 发动机型号
        /// </summary>
        public String EngineNo { get; set; }

        /// <summary>
        /// 发动机编号
        /// </summary>
        public String EngineSerialNo { get; set; }

        /// <summary>
        /// 行驶号码
        /// </summary>
        public String truck_bh { get; set; }

        /// <summary>
        /// 燃油类型
        /// </summary>
        public String FuelType { get; set; }

        /// <summary>
        /// 车辆品牌
        /// </summary>
        public String truck_brand { get; set; }

        /// <summary>
        /// 车辆类型
        /// </summary>
        public String truck_type { get; set; }

        /// <summary>
        /// 车辆颜色
        /// </summary>
        public String truck_color { get; set; }

        /// <summary>
        /// 车辆自重
        /// </summary>
        public String truck_weight { get; set; }

        /// <summary>
        /// 车主姓名
        /// </summary>
        public String ownername { get; set; }


        /// <summary>
        /// 驾驶员联系方式
        /// </summary>
        public String owner_telephone { get; set; }

        /// <summary>
        /// 驾驶员联系方式
        /// </summary>
        public String driver_telephone { get; set; }

        /// <summary>
        /// 司机姓名
        /// </summary>
        public String drivername { get; set; }

        /// <summary>
        /// 服务单位
        /// </summary>
        public String qwdw { get; set; }

        /// <summary>
        /// 发货单位
        /// </summary>
        public String forwarding_unit { get; set; }

        /// <summary>
        /// 收货单位
        /// </summary>
        public String receiving_unit { get; set; }

        /// <summary>
        /// 毛重
        /// </summary>
        public Decimal gross { get; set; }

        /// <summary>
        /// 皮重
        /// </summary>
        public Decimal tare { get; set; }

        /// <summary>
        /// 净重
        /// </summary>
        public Decimal suttle { get; set; }

        /// <summary>
        /// 毛重时间
        /// </summary>
        public DateTime? gross_dtime { get; set; }

        /// <summary>
        /// 皮重时间
        /// </summary>
        public DateTime? tare_dtime { get; set; }

        /// <summary>
        /// 称名
        /// </summary>
        public String weightcname { get; set; }

        /// <summary>
        /// 计划单号
        /// </summary>
        public String planbillcode { get; set; }


        /// <summary>
        /// 毛重
        /// </summary>
        public Decimal erp_gross { get; set; }

        /// <summary>
        /// 皮重
        /// </summary>
        public Decimal erp_tare { get; set; }

        /// <summary>
        /// 净重
        /// </summary>
        public Decimal erp_suttle { get; set; }

        /// <summary>
        /// 净重
        /// </summary>
        public string erp_jlsource { get; set; } = "0";

        /// <summary>
        /// 毛重时间
        /// </summary>
        public DateTime? erp_gross_dtime { get; set; }

        /// <summary>
        /// 皮重时间
        /// </summary>
        public DateTime? erp_tare_dtime { get; set; }

        /// <summary>
        /// 称名
        /// </summary>
        public String erp_weightcname { get; set; }

        /// <summary>
        /// 计划单号
        /// </summary>
        public String erp_planbillcode { get; set; }

        /// <summary>
        /// 合同号
        /// </summary>
        public String erp_contractcode { get; set; }

        /// <summary>
        /// 合同号
        /// </summary>
        public String contractcode { get; set; }

        /// <summary>
        /// ERP生成时间
        /// </summary>
        public DateTime? erpcreatetime { get; set; }

        /// <summary>
        /// ERP车牌号码
        /// </summary>
        public String erptruck_ph { get; set; }

        /// <summary>
        /// ERP随车清单
        /// </summary>
        public String erploadinglist { get; set; }

        /// <summary>
        /// ERP物资名称
        /// </summary>
        public String erpgoodsname { get; set; }

        /// <summary>
        /// ERP物资总量
        /// </summary>
        public Decimal? erpquantity { get; set; }

        /// <summary>
        /// ERP物资单位
        /// </summary>
        public String erpunit { get; set; }

        /// <summary>
        /// 是否计量
        /// </summary>
        public String isjl { get; set; } = "0";


        /// <summary>
        /// 计量信息来源
        /// </summary>
        public String jlsource { get; set; } = "0";

        /// <summary>
        /// 删除标识
        /// </summary>
        public String IsDelete { get; set; }

        /// <summary>
        /// 描述
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
        /// 单据状态0正常1已用，2过期或作废
        /// </summary>
        public String state { get; set; }

        /// <summary>
        /// 车辆信息来源
        /// </summary>
        public String sources { get; set; }

        /// <summary>
        /// 排放标准
        /// </summary>
        public String EmissionStandard { get; set; }

        /// <summary>
        /// 危险品车辆
        /// </summary>
        public String DangerousGoods { get; set; }

        /// <summary>
        /// 环保清单
        /// </summary>
        public String Environmentallist { get; set; }

        /// <summary>
        /// 是否回传钢联物流
        /// </summary>
        public String istoglwl { get; set; } = "0";
        /// <summary>
        /// 是否回传钢联物流
        /// </summary>
        public String isinout { get; set; } = "0";
        /// <summary>
        /// 是否回传钢联物流
        /// </summary>
        public String istoglwlapi { get; set; } = "0";
        /// <summary>
        /// 失败显示推送失败原因
        /// </summary>
        public String Note { get; set; } = "";
        /// <summary>
        /// 是否为旧数据
        /// </summary>
        public String isold { get; set; } = "0";
        /// <summary>
        /// 是否回传钢联物流
        /// </summary>
        public String gross_address { get; set; }
        /// <summary>
        /// 是否回传钢联物流
        /// </summary>
        public String tare_address { get; set; }
        /// <summary>
        /// 是否回传钢联物流
        /// </summary>
        public String recv_port { get; set; }
        /// <summary>
        /// 供货上
        /// </summary>
        public String send_company_name { get; set; }
        /// <summary>
        /// 卸车时间
        /// </summary>
        public DateTime? uploadtime { get; set; }
        /// <summary>
        /// 时间
        /// </summary>
        public DateTime? createtime { get; set; }
        /// <summary>
        /// 计量方式
        /// </summary>
        public string Material_cascade { get; set; }
        /// <summary>
        /// 物资名称ID
        /// </summary>
        public string Thing_id { get; set; }
        /// <summary>
        /// 计量方式
        /// </summary>
        public string Metering { get; set; }
        /// <summary>
        /// 物资名称ID
        /// </summary>
        public string WZ_NUM { get; set; }
        /// <summary>
        /// 物资名称ID
        /// </summary>
        public string WZ_UNIT { get; set; }
        /// <summary>
        /// 是否取消
        /// </summary>
        public string IsCancel { get; set; } = "0";
        /// <summary>
        /// 是否取消
        /// </summary>
        public string IsUpdate { get; set; } = "0";
        /// <summary>
        /// 是否检测滞留
        /// </summary>
        public string IsCheckZL { get; set; } = "1";

    }
}