import React from 'react';
import { ProgressState } from '../types/audit';
import { Loader2, CheckCircle2, AlertOctagon } from 'lucide-react';

interface SignalRProgressBarProps {
  progress: ProgressState;
}

export const SignalRProgressBar: React.FC<SignalRProgressBarProps> = ({ progress }) => {
  if (!progress.isProcessing && progress.percent === 0 && !progress.error) {
    return null;
  }

  const isFailed = progress.stage === 'Failed' || !!progress.error;
  const isCompleted = progress.percent === 100;

  return (
    <div className="bg-slate-900/60 border border-slate-800/90 rounded-2xl p-4 backdrop-blur-md shadow-xl my-4">
      <div className="flex items-center justify-between mb-2">
        <div className="flex items-center space-x-2">
          {isFailed ? (
            <AlertOctagon className="w-5 h-5 text-rose-400" />
          ) : isCompleted ? (
            <CheckCircle2 className="w-5 h-5 text-emerald-400" />
          ) : (
            <Loader2 className="w-5 h-5 text-cyan-400 animate-spin" />
          )}
          <span className="text-xs font-bold text-white uppercase tracking-wider">
            {progress.stage}
          </span>
          <span className="text-xs text-slate-400 font-medium">({progress.percent}%)</span>
        </div>
        <span className="text-xs text-slate-300 font-medium">{progress.message}</span>
      </div>

      <div className="w-full bg-slate-950 rounded-full h-2 overflow-hidden border border-slate-800">
        <div
          className={`h-full transition-all duration-300 rounded-full ${
            isFailed
              ? 'bg-rose-500'
              : isCompleted
              ? 'bg-emerald-400'
              : 'bg-gradient-to-r from-cyan-500 to-blue-500'
          }`}
          style={{ width: `${progress.percent}%` }}
        />
      </div>
    </div>
  );
};
