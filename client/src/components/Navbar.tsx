import React from 'react';
import { ShieldCheck, Cpu, Wifi, WifiOff } from 'lucide-react';

interface NavbarProps {
  activeTab: 'scenarios' | 'upload';
  setActiveTab: (tab: 'scenarios' | 'upload') => void;
  signalRConnected: boolean;
}

export const Navbar: React.FC<NavbarProps> = ({ activeTab, setActiveTab, signalRConnected }) => {
  return (
    <header className="border-b border-slate-800/80 bg-slate-900/60 backdrop-blur-md sticky top-0 z-40">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
        <div className="flex items-center space-x-3">
          <div className="w-10 h-10 rounded-xl bg-gradient-to-tr from-cyan-500 to-emerald-400 p-0.5 shadow-lg shadow-cyan-500/20">
            <div className="w-full h-full bg-slate-950 rounded-[10px] flex items-center justify-center">
              <ShieldCheck className="w-5 h-5 text-cyan-400" />
            </div>
          </div>
          <div>
            <div className="flex items-center space-x-2">
              <span className="font-extrabold text-lg tracking-tight bg-gradient-to-r from-white via-slate-100 to-slate-400 bg-clip-text text-transparent">
                AuditGuard<span className="text-cyan-400">.AI</span>
              </span>
              <span className="text-[10px] uppercase font-bold tracking-wider px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 flex items-center gap-1">
                <Cpu className="w-2.5 h-2.5" /> 100% On-Premise
              </span>
            </div>
            <p className="text-[11px] text-slate-400">Kiểm soát rủi ro & Đối soát chứng từ 3 chiều tự động</p>
          </div>
        </div>

        {/* Tab chuyển đổi */}
        <div className="flex items-center space-x-1 bg-slate-950/80 p-1 rounded-xl border border-slate-800">
          <button
            onClick={() => setActiveTab('scenarios')}
            className={`px-4 py-1.5 rounded-lg text-xs font-semibold transition-all duration-200 ${
              activeTab === 'scenarios'
                ? 'bg-gradient-to-r from-cyan-500 to-blue-600 text-white shadow-md shadow-cyan-500/25'
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            20 Kịch bản mẫu Demo
          </button>
          <button
            onClick={() => setActiveTab('upload')}
            className={`px-4 py-1.5 rounded-lg text-xs font-semibold transition-all duration-200 ${
              activeTab === 'upload'
                ? 'bg-gradient-to-r from-cyan-500 to-blue-600 text-white shadow-md shadow-cyan-500/25'
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            Tải lên 3 tệp chứng từ
          </button>
        </div>

        {/* Trạng thái SignalR */}
        <div className="flex items-center space-x-2 text-xs">
          <div className={`flex items-center space-x-1.5 px-2.5 py-1 rounded-full border ${
            signalRConnected 
              ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' 
              : 'bg-rose-500/10 border-rose-500/20 text-rose-400'
          }`}>
            <span className={`w-2 h-2 rounded-full ${signalRConnected ? 'bg-emerald-400 animate-pulse' : 'bg-rose-400'}`} />
            {signalRConnected ? (
              <span className="flex items-center gap-1 font-medium"><Wifi className="w-3 h-3" /> Realtime Live</span>
            ) : (
              <span className="flex items-center gap-1 font-medium"><WifiOff className="w-3 h-3" /> Mất kết nối Hub</span>
            )}
          </div>
        </div>
      </div>
    </header>
  );
};
