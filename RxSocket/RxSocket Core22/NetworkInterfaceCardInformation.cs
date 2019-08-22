using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace RxSocket
{
	public class NetworkInterfaceCardInformation
	{
		public static NetworkInterfaceCardInformation[] GetNetworkInterfaceCardInformations()
		{
			var nics = NetworkInterface.GetAllNetworkInterfaces();
			if (nics.Length <= 0) return null;
			return nics.Select(nic => new NetworkInterfaceCardInformation(nic)).ToArray();
		}

		public string Name { get; }
		public string Description { get; }
		public PhysicalAddress PhysicalAddress { get; }
		public OperationalStatus OperationalStatus { get; }
		public NetworkInterfaceType NetworkInterfaceType { get; }

		public bool IsDnsEnabled { get; }
		public bool IsDynamicDnsEnabled { get; }
		public bool IsReceiveOnly { get; }
		public bool SupportsMulticast { get; }
		public bool IsSupportIPv4 { get; }
		public bool IsSupportIPv6 { get; }

		public IPProperty IPv4Property { get; }
		public IPProperty IPv6Property { get; }

		public IPAddress[] DhcpAddresses { get; }
		public IPAddress[] DnsAddresses { get; }
		public IPAddress[] GatewayAddresses { get; }
		public AddressAndMask[] UnicastAddresses { get; }

		public class AddressAndMask
		{
			public IPAddress Address { get; }
			public IPAddress IPv4Mask { get; }

			internal AddressAndMask(UnicastIPAddressInformation unicastIPAddressInformation)
			{
				Address = unicastIPAddressInformation.Address;
				IPv4Mask = unicastIPAddressInformation.IPv4Mask;
			}
			public override string ToString() => $"{Address}({IPv4Mask})";
		}

		public class IPProperty
		{
			public int Mtu { get; }
			public int Index { get; }

			internal IPProperty(IPv4InterfaceProperties properties)
			{
				Mtu = properties.Mtu;
				Index = properties.Index;
			}

			internal IPProperty(IPv6InterfaceProperties properties)
			{
				Mtu = properties.Mtu;
				Index = properties.Index;
			}
		}


		internal NetworkInterfaceCardInformation(NetworkInterface networkInterface)
		{
			Name = networkInterface.Name;
			Description = networkInterface.Description;
			PhysicalAddress = networkInterface.GetPhysicalAddress();
			OperationalStatus = networkInterface.OperationalStatus;
			NetworkInterfaceType = networkInterface.NetworkInterfaceType;

			var properties = networkInterface.GetIPProperties();
			DhcpAddresses = properties.DhcpServerAddresses.ToArray();
			DnsAddresses = properties.DnsAddresses.ToArray();
			GatewayAddresses = properties.GatewayAddresses.Select(address => address.Address).ToArray();
			UnicastAddresses = properties.UnicastAddresses.Select(address => new AddressAndMask(address)).ToArray();
			IsDnsEnabled = properties.IsDnsEnabled;
			IsDynamicDnsEnabled = properties.IsDynamicDnsEnabled;
			IsReceiveOnly = networkInterface.IsReceiveOnly;
			SupportsMulticast = networkInterface.SupportsMulticast;
			IsSupportIPv4 = networkInterface.Supports(NetworkInterfaceComponent.IPv4);
			IsSupportIPv6 = networkInterface.Supports(NetworkInterfaceComponent.IPv6);
			if (IsSupportIPv4) IPv4Property = new IPProperty(properties.GetIPv4Properties());
			if (IsSupportIPv6) IPv6Property = new IPProperty(properties.GetIPv6Properties());
		}

		public override string ToString() => Name;
		public string ToMultiLineString()
		{
			return $"{Description}{Environment.NewLine}" +
				$"==================================={Environment.NewLine}" +
				$" Name                        : {Name}{Environment.NewLine}" +
				$" PhysicalAddress             : {BitConverter.ToString(PhysicalAddress.GetAddressBytes())}{Environment.NewLine}" +
				$" OperationalStatus           : {OperationalStatus}{Environment.NewLine}" +
				$" NetworkInterfaceType        : {NetworkInterfaceType}{Environment.NewLine}" +
				$" DNS enable                  : {IsDnsEnabled}{Environment.NewLine}" +
				$" Dynamically configured DNS  : {IsDynamicDnsEnabled}{Environment.NewLine}" +
				$" Receive Only                : {IsReceiveOnly}{Environment.NewLine}" +
				$" Multicast                   : {SupportsMulticast}{Environment.NewLine}" +
				$" IPv4                        : {IsSupportIPv4}{Environment.NewLine}" +
				(IsSupportIPv4 ? $"   MTU                       : {IPv4Property.Mtu}{Environment.NewLine}" : "") +
				(IsSupportIPv4 ? $"   Index                     : {IPv4Property.Index}{Environment.NewLine}" : "") +
				$" IPv6                        : {IsSupportIPv6}{Environment.NewLine}" +
				(IsSupportIPv6 ? $"   MTU                       : {IPv6Property.Mtu}{Environment.NewLine}" : "") +
				(IsSupportIPv6 ? $"   Index                     : {IPv6Property.Index}{Environment.NewLine}" : "") +
				$" DHCP Addresses              : {string.Join(" / ", DhcpAddresses.Select(address => address.ToString()))}{Environment.NewLine}" +
				$" DNS Addresses               : {string.Join(" / ", DnsAddresses.Select(address => address.ToString()))}{Environment.NewLine}" +
				$" Gateway Addresses           : {string.Join(" / ", GatewayAddresses.Select(address => address.ToString()))}{Environment.NewLine}" +
				$" Unicast Addresses           : {string.Join(" / ", UnicastAddresses.Select(address => address.ToString()))}";
		}
	}
}
