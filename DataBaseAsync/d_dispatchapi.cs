using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// Api派单缓存
    /// </summary>
    [Table("d_dispatchapi")]
    public class d_dispatchapi
    {

        /// <summary>
        /// Id
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 预约状态
        /// </summary>
        public String state { get; set; }

        /// <summary>
        /// 车牌号码
        /// </summary>
        public String truck_ph { get; set; }

        /// <summary>
        /// 车牌颜色
        /// </summary>
        public String ph_color { get; set; }

        /// <summary>
        /// 通行日期
        /// </summary>
        public DateTime passagedate { get; set; }

        /// <summary>
        /// 物资种类一级
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
        /// 备注
        /// </summary>
        public String note { get; set; }

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
        public Decimal? gross { get; set; }

        /// <summary>
        /// 皮重
        /// </summary>
        public Decimal? tare { get; set; }

        /// <summary>
        /// 净重
        /// </summary>
        public Decimal? suttle { get; set; }

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
        /// 合同号
        /// </summary>
        public String contractcode { get; set; }

        /// <summary>
        /// 原单号
        /// </summary>
        public String truck_xh { get; set; }

        /// <summary>
        /// 作废标识
        /// </summary>
        public String invalidflag { get; set; }

        /// <summary>
        /// 删除标识
        /// </summary>
        public String IsDelete { get; set; }

        /// <summary>
        /// 来源钢源 13钢联
        /// </summary>
        public String sources { get; set; }

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
        /// truck_xh_update
        /// </summary>
        public String truck_xh_update { get; set; }

        /// <summary>
        /// driver_name
        /// </summary>
        public String driver_name { get; set; }

        /// <summary>
        /// driver_sfzh
        /// </summary>
        public String driver_sfzh { get; set; }

        /// <summary>
        /// driver_tel
        /// </summary>
        public String driver_tel { get; set; }

        /// <summary>
        /// isjl
        /// </summary>
        public String isjl { get; set; }

        /// <summary>
        /// 审核人
        /// </summary>
        public String SHR_ID { get; set; }

        /// <summary>
        /// 审核部门
        /// </summary>
        public String SHR_DEPART { get; set; }

        /// <summary>
        /// 北斗ID
        /// </summary>
        public String beidou_id { get; set; }

        /// <summary>
        /// 是否安装北斗
        /// </summary>
        public String beidou_positioning { get; set; }

        /// <summary>
        /// driverid
        /// </summary>
        public String driverid { get; set; }

        /// <summary>
        /// truckid
        /// </summary>
        public String truckid { get; set; }

        /// <summary>
        /// lywzid
        /// </summary>
        public String lywzid { get; set; }

        /// <summary>
        /// wzweightm
        /// </summary>
        public String wzweightm { get; set; }

        /// <summary>
        /// transportation
        /// </summary>
        public String transportation { get; set; }

        /// <summary>
        /// updateid
        /// </summary>
        public String updateid { get; set; }

        /// <summary>
        /// tasktime
        /// </summary>
        public DateTime? tasktime { get; set; }

        /// <summary>
        /// DISPATCH_NUM
        /// </summary>
        public String DISPATCH_NUM { get; set; }

        /// <summary>
        /// CANCEL_NUM
        /// </summary>
        public String CANCEL_NUM { get; set; }

        /// <summary>
        /// WZ_NUM
        /// </summary>
        public String WZ_NUM { get; set; }

        /// <summary>
        /// WZ_UNIT
        /// </summary>
        public String WZ_UNIT { get; set; }

    }
}