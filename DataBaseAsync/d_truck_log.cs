
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 车辆进出日志
    /// </summary>
    [Table("d_truck_log")]
    public class d_truck_log
    {
        public d_truck_log()
        {
        }


        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 车牌号码
        /// </summary>
        public String truck_ph { get; set; }

        /// <summary>
        /// 车牌颜色
        /// </summary>
        public String ph_color { get; set; }

        /// <summary>
        /// 车辆卡类型
        /// </summary>
        public String zl { get; set; }

        /// <summary>
        /// 车辆照片
        /// </summary>
        public String photourl { get; set; }

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
        /// 车辆状态
        /// </summary>
        public String gsbz { get; set; }

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
        /// 行驶证号码
        /// </summary>
        public String truck_bh { get; set; }

        /// <summary>
        /// 车主姓名
        /// </summary>
        public String owner_id { get; set; }

        /// <summary>
        /// 车主身份证
        /// </summary>
        public String owner_sfzh { get; set; }

        /// <summary>
        /// 车主联系方式
        /// </summary>
        public String owner_telephone { get; set; }


        /// <summary>
        /// 车辆资产
        /// </summary>
        public String truck_assets { get; set; }

        /// <summary>
        /// 车辆信息来源fman_name
        /// </summary>
        public String sources { get; set; }

        /// <summary>
        /// 服务单位
        /// </summary>
        public String qwdw { get; set; }

        /// <summary>
        /// 所属单位
        /// </summary>
        public String dwdm { get; set; }

        /// <summary>
        /// 门岗
        /// </summary>
        public String door { get; set; }

        /// <summary>
        /// 门岗名称，不是表字段，忽略查询
        /// </summary>
        [NotMapped]
        public string doorname { get; set; }

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
        /// 通行标识
        /// </summary>
        public int passflag { get; set; }

        /// <summary>
        /// 中间表ID
        /// </summary>
        public string tempid { get; set; }

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

    }
}